// Cross-language checks against the real C# BridgeView, driven through this suite's --peer mode (Peer.cs):
//   1. interop: the real TS PageSession (compiled from Injection~/src/bridge) <-> BridgeView;
//   2. e2e: the BUILT injection.js, in the M0 page shim <-> BridgeView;
//   3. M3: the TS page's ACCEPT, item numbering and read-your-writes against BridgeView's.
// usage: node interop.mjs [path/to/injection.js]        (default: Runtime/Resources/injection.js)
// Builds what it needs into a temp folder: this suite (dotnet build) and the TS bridge (the injection package's tsc).
import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import vm from 'node:vm';

const here = dirname(fileURLToPath(import.meta.url));
const sdkDir = resolve(here, '../..');
const injectionDir = resolve(sdkDir, '../com.sidequest.injection/Injection~');
const bundlePath = process.argv[2] ? resolve(process.argv[2]) : join(sdkDir, 'Runtime/Resources/injection.js');
const tmp = mkdtempSync(join(tmpdir(), 'bridge-interop-'));
const peers = [];

function build() {
    const d = spawnSync('dotnet', ['build', join(here, 'BridgeCoreTests.csproj'), '-c', 'Release', '-o', join(tmp, 'dotnet'), '-v', 'q', '-nologo'], { stdio: 'inherit' });
    if (d.status !== 0) throw new Error('dotnet build failed');
    const tsc = createRequire(join(injectionDir, 'package.json')).resolve('typescript/bin/tsc');
    const t = spawnSync(process.execPath, [tsc, '-p', join(injectionDir, 'src/bridge/test/tsconfig.unit.json'), '--outDir', join(tmp, 'ts')], { stdio: 'inherit' });
    if (t.status !== 0) throw new Error('tsc failed');
}

/** One C# BridgeView in a child process. cmd(c, arg) sends a line and returns the messages it posted to the page. */
function startPeer(calls = false, extraEnv = {}) {
    const env = { ...process.env, ...(calls ? { BRIDGE_PEER_CALLS: '1' } : {}), ...extraEnv };
    const proc = spawn('dotnet', [join(tmp, 'dotnet', 'BridgeCoreTests.dll'), '--peer'], { stdio: ['pipe', 'pipe', 'inherit'], env });
    const exited = new Promise((r) => proc.on('exit', r));
    peers.push({ proc, exited });
    let buf = '';
    const lines = [];
    const ran = [];
    const called = [];
    const status = [];
    let waiter = null;
    proc.stdout.setEncoding('utf8');
    proc.stdout.on('data', (d) => {
        buf += d;
        let i;
        while ((i = buf.indexOf('\n')) >= 0) { lines.push(buf.substring(0, i).replace(/\r$/, '')); buf = buf.substring(i + 1); }
        if (waiter && lines.includes('.')) { const w = waiter; waiter = null; w(); }
    });
    async function cmd(c, arg) {
        // ASCII-only JSON: the peer's console input encoding is not UTF-8 by default on Windows.
        const json = arg === undefined ? '' : JSON.stringify(arg).replace(/[\u007f-￿]/g, (ch) => '\\u' + ch.charCodeAt(0).toString(16).padStart(4, '0'));
        proc.stdin.write(arg === undefined ? c + '\n' : c + ' ' + json + '\n');
        if (!lines.includes('.')) await new Promise((r) => { waiter = r; });
        const out = [];
        while (lines.length) {
            const l = lines.shift();
            if (l === '.') break;
            if (l.startsWith('O ')) out.push(JSON.parse(l.substring(2)));
            else if (l.startsWith('X ')) { const sp = l.indexOf(' ', 2); ran.push({ epoch: Number(l.substring(2, sp)), text: JSON.parse(l.substring(sp + 1)) }); }
            else if (l.startsWith('C ')) { const sp = l.indexOf(' ', 2); called.push({ member: Number(l.substring(2, sp)), args: JSON.parse(l.substring(sp + 1)) }); }
            else if (l.startsWith('Z ')) status.push(JSON.parse(l.substring(2)));
        }
        return out;
    }
    return { cmd, ran, called, status, quit: () => { proc.stdin.end('Q\n'); return exited; } };
}

let checks = 0;
function check(name, fn) { fn(); checks++; console.log('  ok   ' + name); }

class Host {
    constructor() { this.out = []; this.tasks = []; this.t = 0; this.timers = new Map(); this.nextTimer = 1; }
    post(d) { this.out.push(d); }
    now() { return this.t; }
    schedule(fn) { this.tasks.push(fn); }
    setTimer(fn) { const id = this.nextTimer++; this.timers.set(id, fn); return id; }
    clearTimer(id) { this.timers.delete(id); }
    run() { while (this.tasks.length) this.tasks.shift()(); }
    /** Runs the timers now armed, as if their time had come. */
    fire() { const due = [...this.timers.values()]; this.timers.clear(); for (const fn of due) fn(); }
}

async function interop() {
    const B = createRequire(import.meta.url)(join(tmp, 'ts', 'Injection~', 'src', 'bridge', 'index.js'));
    const { cmd, ran, quit } = startPeer();
    async function pump(host, page, rounds = 40) {
        for (let i = 0; i < rounds; i++) {
            host.run();
            const up = host.out; host.out = [];
            for (const d of up) await cmd('R', d);
            const down = await cmd('D');
            for (const d of down) page.receive(d);
            host.run();
            if (up.length === 0 && down.length === 0 && host.out.length === 0) return;
        }
    }
    function makePage(token, href, got) {
        const host = new Host();
        const page = new B.PageSession(host, (t) => got.push(t), { href, isTop: true, sdkVersion: 'interop', docToken: token, heartbeatMs: 0 });
        return { host, page };
    }

    await cmd('L', 'https://w.example/a');
    await cmd('N');
    const got1 = [];
    const { host: h1, page: p1 } = makePage('tok-1', 'https://w.example/a', got1);
    p1.sendText('!sst!');
    p1.sendText('!i!:1¶!gp!¶');
    await pump(h1, p1);
    check('handshake TS page <-> C# view; queued ops dispatched in order; reply delivered', () => {
        assert.equal(p1.state, 'open');
        assert.equal(p1.epoch, 1);
        assert.deepEqual(ran.map((r) => r.text), ['!sst!', '!i!:1¶!gp!¶']);
        assert.ok(ran.every((r) => r.epoch === 1));
        assert.deepEqual(got1, ['!i!:1¶ok-1']);
    });

    const burst = [];
    for (let i = 0; i < 3000; i++) burst.push('!st!¶' + i + '§' + 'w'.repeat(i % 60) + (i % 7 === 0 ? '😀' : ''));
    ran.length = 0;
    for (const t of burst) p1.sendText(t);
    await pump(h1, p1);
    check('3000-op burst page -> Unity: same strings, same order, frames only', () => {
        assert.deepEqual(ran.map((r) => r.text), burst);
    });

    const events = [];
    for (let i = 0; i < 500; i++) events.push('!e!tu!¶' + i);
    got1.length = 0;
    for (const e of events) await cmd('S', e);
    await pump(h1, p1);
    check('500 events Unity -> page in order through text frames', () => assert.deepEqual(got1, events));

    const big = '!e!big¶' + 'B'.repeat(3 * 1024 * 1024);
    got1.length = 0;
    await cmd('S', big);
    await pump(h1, p1, 200);
    check('a 3 M-char message Unity -> page arrives whole, once, on frames', () => {
        assert.equal(p1.state, 'open');
        assert.deepEqual(got1, [big]);
    });

    p1.sendText('!i!:7¶!slow¶');
    await pump(h1, p1);
    // Navigation: Unity closes doc 1 (CLOSE goes to page 1), page 2 opens epoch 2 and reuses id 7.
    for (const d of await cmd('L', 'https://w.example/b')) p1.receive(d);
    await cmd('N');
    const got2 = [];
    const { host: h2, page: p2 } = makePage('tok-2', 'https://w.example/b', got2);
    p2.sendText('!i!:7¶!gp!¶');
    await pump(h2, p2);
    const late = await cmd('F'); // doc 1's handler finishes now
    await cmd('D');
    for (const d of late) p2.receive(d);
    await pump(h2, p2);
    check('navigation mid-request: doc 1 closed and parked, doc 2 gets exactly its own reply, the late one is dropped', () => {
        assert.equal(p1.state, 'parked', 'the replaced page parks its strings (never replayed into doc 2)');
        assert.equal(p2.epoch, 2);
        assert.deepEqual(got2, ['!i!:7¶ok-2']);
    });

    // A lost HELLO: nothing else carries the page's ops, so it says HELLO again.
    await cmd('L', 'https://w.example/c');
    await cmd('N');
    ran.length = 0;
    const got3 = [];
    const { host: h3, page: p3 } = makePage('tok-3', 'https://w.example/c', got3);
    p3.sendText('!sst!');
    p3.sendText('!i!:1¶!gp!¶');
    h3.run();
    h3.out.length = 0; // lost
    h3.fire();
    await pump(h3, p3);
    check('lost HELLO: said again, opens, the queue runs in the session, in order, and the request is answered', () => {
        assert.equal(p3.state, 'open');
        assert.equal(p3.stats.helloRetries, 1);
        assert.deepEqual(ran.map((r) => r.text), ['!sst!', '!i!:1¶!gp!¶']);
        assert.ok(ran.every((r) => r.epoch === p3.epoch));
        assert.deepEqual(got3, ['!i!:1¶ok-' + p3.epoch]);
    });

    // A RESET: a frame with a seq gap. The TS page says HELLO again; the C# view keeps the document.
    ran.length = 0;
    got3.length = 0;
    const epoch3 = p3.epoch;
    h3.t = 1e6; // the session has lasted: HELLO goes at once
    p3.sendText('!i!:2¶!slow¶');
    await pump(h3, p3);
    await cmd('R', B.encodeTextCarrier({ kind: B.Kind.DATA, flags: B.COMPLETE, epoch: epoch3, seq: 999, ack: 0, length: 0 }, B.packEntries(['x'])));
    await cmd('S', '!e!queued-while-resetting');
    for (const d of await cmd('D')) p3.receive(d);
    const resetState = p3.state;
    p3.sendText('!after-reset!');
    await pump(h3, p3);
    await cmd('F'); // the request's handler finishes now, in its document's context
    for (const d of await cmd('D')) p3.receive(d);
    h3.run();
    check('RESET: the same epoch again, nothing lost or repeated, the reply in flight still arrives', () => {
        assert.equal(resetState, 'hello');
        assert.equal(p3.state, 'open');
        assert.equal(p3.epoch, epoch3);
        assert.equal(p3.stats.reopens, 1);
        assert.deepEqual(ran.map((r) => r.text), ['!i!:2¶!slow¶', '!after-reset!']);
        assert.ok(ran.every((r) => r.epoch === epoch3));
        assert.deepEqual(got3, ['!e!queued-while-resetting', '!i!:2¶ok-' + epoch3]);
    });

    // A page that loses its place while Unity resets it: each has given up on the other, and frames are on their way.
    got3.length = 0;
    h3.t = 2e6;
    p3.sendText('!i!:3¶!slow¶');
    await pump(h3, p3);
    await cmd('F');
    await cmd('S', '!e!crossed');
    const onTheirWay = await cmd('D');
    assert.ok(onTheirWay.length >= 1);
    const gap = () => B.encodeTextCarrier({ kind: B.Kind.DATA, flags: B.COMPLETE, epoch: epoch3, seq: 999, ack: 0, length: 0 }, B.packEntries(['x']));
    p3.receive(gap());                        // the page breaks first: HELLO, not yet read
    const crossingState = p3.state;
    await cmd('R', gap());                    // and Unity resets it
    const reset = await cmd('D');
    for (const d of onTheirWay) p3.receive(d);  // dropped: the page is saying HELLO
    for (const d of reset) p3.receive(d);
    const droppedMeanwhile = got3.length;
    await pump(h3, p3);
    check('a HELLO crossing a RESET: the reply and event the page dropped go again, once, in order', () => {
        assert.equal(crossingState, 'hello');
        assert.equal(droppedMeanwhile, 0);
        assert.equal(p3.state, 'open');
        assert.equal(p3.epoch, epoch3);
        assert.deepEqual(got3, ['!i!:3¶ok-' + epoch3, '!e!crossed']);
    });

    // A revival that waits behind a load that never finishes: closed for good, not held for ever.
    for (const d of await cmd('L', 'https://w.example/never-commits')) p3.receive(d);
    await cmd('N');
    h3.fire(); // the park grace: p3 asks again
    await pump(h3, p3);
    const revivedState = p3.state;
    await cmd('T', 60000);
    await cmd('D'); // the hold times out and, a revival with a load pending, it stays held
    await cmd('T', 60000);
    for (const d of await cmd('D')) p3.receive(d);
    check('a revival held behind a load that never finishes is closed for good, and told', () => {
        assert.equal(revivedState, 'open');
        assert.equal(p3.state, 'closed');
    });
    await quit();
}

async function e2eBundle() {
    const { createPageContext } = await import(pathToFileURL(join(injectionDir, 'test/surface/load-bundle.mjs')).href);
    const { cmd, ran, quit } = startPeer();
    const { context } = createPageContext();
    const sent = [];
    const timeouts = [];
    const oraListeners = [];
    let rnd = 7;
    context.crypto = { getRandomValues: (b) => { for (let i = 0; i < b.length; i++) { rnd = (rnd * 1103515245 + 12345) >>> 0; b[i] = rnd >>> 24; } return b; }, randomUUID: () => '00000000-0000-4000-8000-000000000001' };
    context.setTimeout = (fn, ms) => { timeouts.push({ fn, ms: ms || 0 }); return timeouts.length; };
    context.setInterval = () => { throw new Error('the bridge must not start an interval timer'); };
    context.O = { sendMessage: (type, data) => { assert.equal(type, 'custom-message'); sent.push(String(data)); }, messages: { On: (n, fn) => oraListeners.push(fn) } };
    context.location.href = 'https://world.example/space';
    vm.runInContext(readFileSync(bundlePath, 'utf8'), context, { filename: 'injection.js' });
    const BS = context.BS;
    const runZero = () => { for (let g = 0; g < 1000; g++) { const i = timeouts.findIndex((t) => t.ms <= 0); if (i < 0) return; timeouts.splice(i, 1)[0].fn(); } };
    async function pump(rounds = 30) {
        for (let r = 0; r < rounds; r++) {
            runZero();
            const up = sent.splice(0);
            for (const d of up) await cmd('R', d);
            const down = await cmd('D');
            for (const d of down) for (const l of oraListeners.slice()) l({ detail: { id: '', type: 'default-message', data: d } });
            await new Promise((r2) => setImmediate(r2));
            runZero();
            if (up.length === 0 && down.length === 0 && sent.length === 0) return;
        }
    }
    await cmd('L', 'https://world.example/space');
    await cmd('N');
    const scene = new BS.Scene();
    const platform = scene.GetPlatform();
    scene.SetCanMove(false);
    await pump();
    const p = await Promise.race([platform, new Promise((r) => setTimeout(() => r('TIMEOUT'), 2000))]);
    check('built bundle <-> C# view: handshake, 3 ops in epoch 1, GetPlatform answered in a frame', () => {
        assert.equal(context.__bsBridge.session.state, 'open');
        assert.equal(context.__bsBridge.session.epoch, 1);
        assert.deepEqual(ran.map((r) => r.text.replace(/!i!:\d+/, '!i!:N')), ['!sst!', '!i!:N¶!gp!¶', '!i!:N¶!scm!¶0']);
        assert.ok(ran.every((r) => r.epoch === 1), 'dispatched in the document\'s session');
        assert.equal(p, 'ok-1');
    });
    await quit();
}

/** The real TS client against the real C# call host, with FakeApi's table standing in for the generated one. */
async function typedCalls() {
    const req = createRequire(import.meta.url);
    const B = req(join(tmp, 'ts', 'Injection~', 'src', 'bridge', 'index.js'));
    const C = req(join(tmp, 'ts', 'Injection~', 'src', 'unity', 'client.js'));
    const open = async (calls) => {
        const peer = startPeer(calls);
        await peer.cmd('L', 'https://w.example/a');
        await peer.cmd('N');
        const host = new Host();
        const page = new B.PageSession(host, () => {}, { href: 'https://w.example/a', isTop: true, sdkVersion: 'interop', docToken: 'tok', heartbeatMs: 0 });
        C.useSession(page);
        const pump = async () => {
            for (let i = 0; i < 40; i++) {
                host.run();
                const up = host.out; host.out = [];
                for (const d of up) await peer.cmd('R', d);
                const down = await peer.cmd('D');
                for (const d of down) page.receive(d);
                host.run();
                await new Promise((r) => setImmediate(r));
                if (up.length === 0 && down.length === 0 && host.out.length === 0) return;
            }
        };
        return { ...peer, host, page, pump };
    };
    const settled = (p) => p.then((value) => ({ value }), (error) => ({ code: error.code, message: error.message }));

    // FakeApi's ids, as CallTests.cs declares them.
    const Get = 1, Set = 2, Global = 3, Denied = 4, Echo = 6, Throws = 7, ReturnsOutsider = 8, Sum = 10;
    const obj = (h) => new C.BridgeObject(h);
    const t = await open(true);
    const results = {
        set: settled(C.call(obj(1001), Set, 'f', [4.5])),
        get: settled(C.call(obj(1001), Get, '', [])),
        dead: settled(C.call(obj(1002), Get, '', [])),
        outside: settled(C.call(obj(1003), Set, 'f', [1])),
        outsideArg: settled(C.call(null, Echo, 'r', [obj(1003)])),
        unknown: settled(C.call(obj(777), Get, '', [])),
        denied: settled(C.call(null, Denied, '', [])),
        range: settled(C.call(null, Global, 'f', [11])),
        throws: settled(C.call(null, Throws, '', [])),
        echo: settled(C.call(null, Echo, 'r', [obj(1001)])),
        filtered: settled(C.call(null, ReturnsOutsider, '', [])),
        sum: settled(C.call(null, Sum, 'f[]', [[1, 2.5]])),
        nope: settled(C.call(null, 999, '', [])),
    };
    await t.pump();
    const r = {};
    for (const k of Object.keys(results)) r[k] = await results[k];
    check('typed calls TS client <-> C# host: effect, then one reply, each with its typed outcome', () => {
        assert.equal(t.page.families, 1);
        assert.equal(t.page.caps, 7);
        assert.equal(r.set.value, null);
        assert.equal(r.get.value, 4.5);
        assert.equal(r.dead.code, 'DESTROYED');
        assert.equal(r.outside.code, 'DENIED');
        assert.match(r.outside.message, /does not belong to this world/);
        assert.equal(r.outsideArg.code, 'DENIED');
        assert.equal(r.unknown.code, 'UNKNOWN_HANDLE');
        assert.equal(r.denied.code, 'DENIED');
        assert.equal(r.range.code, 'DENIED');
        assert.equal(r.throws.code, 'INTERNAL');
        assert.match(r.throws.message, /boom/);
        assert.equal(r.echo.value.unityId, 1001);
        assert.equal(r.filtered.value, null);
        assert.equal(r.sum.value, 3.5);
        assert.equal(r.nope.code, 'UNKNOWN_MEMBER');
    });
    const burst = [];
    for (let i = 0; i < 1000; i++) burst.push(settled(C.call(null, Sum, 'f[]', [[i]])));
    await t.pump();
    await t.pump();
    await t.pump();
    await t.pump();
    const answers = await Promise.all(burst);
    check('a 1000-call burst through the window: all answered, in order, none refused', () => {
        assert.deepEqual(answers.map((a) => a.value), burst.map((_, i) => i));
    });
    await t.quit();

    const old = await open(false);
    const refused = settled(C.call(obj(1001), Get, '', []));
    await old.pump();
    const e = await refused;
    check('a client with no call host: rejected UNSUPPORTED, and the page never sent the CALL', () => {
        assert.equal(old.page.state, 'open');
        assert.equal(old.page.families, 0);
        assert.equal(e.code, 'UNSUPPORTED');
    });
    await old.quit();
    C.useSession(null);
}

/** The built bundle's BS.Unity against the C# host: generated ids, through window.BS, down to a thunk and back. */
async function e2eUnity() {
    const { createPageContext } = await import(pathToFileURL(join(injectionDir, 'test/surface/load-bundle.mjs')).href);
    const { cmd, called, quit } = startPeer(true);
    const { context } = createPageContext();
    const sent = [];
    const timeouts = [];
    const oraListeners = [];
    context.crypto = { getRandomValues: (b) => { for (let i = 0; i < b.length; i++) b[i] = i * 7; return b; }, randomUUID: () => '00000000-0000-4000-8000-000000000001' };
    context.setTimeout = (fn, ms) => { timeouts.push({ fn, ms: ms || 0 }); return timeouts.length; };
    context.O = { sendMessage: (type, data) => { sent.push(String(data)); }, messages: { On: (n, fn) => oraListeners.push(fn) } };
    context.location.href = 'https://world.example/space';
    vm.runInContext(readFileSync(bundlePath, 'utf8'), context, { filename: 'injection.js' });
    const BS = context.BS;
    const runZero = () => { for (let g = 0; g < 1000; g++) { const i = timeouts.findIndex((t) => t.ms <= 0); if (i < 0) return; timeouts.splice(i, 1)[0].fn(); } };
    async function pump(rounds = 30) {
        for (let r = 0; r < rounds; r++) {
            runZero();
            const up = sent.splice(0);
            for (const d of up) await cmd('R', d);
            const down = await cmd('D');
            for (const d of down) for (const l of oraListeners.slice()) l({ detail: { id: '', type: 'default-message', data: d } });
            await new Promise((r2) => setImmediate(r2));
            runZero();
            if (up.length === 0 && down.length === 0 && sent.length === 0) return;
        }
    }
    const policy = JSON.parse(readFileSync(join(sdkDir, 'Bridge~/unity-api.policy.json'), 'utf8'));
    const idOf = (test) => policy.members.find(test).id;
    const addForce = idOf((m) => m.sig === 'UnityEngine.Rigidbody.AddForce(UnityEngine.Vector3,UnityEngine.ForceMode)');
    const raycast = idOf((m) => m.curated === 'Physics_RaycastAll' && m.params.length === 3);
    const lookup = idOf((m) => m.curated === 'GameObject_GetComponent');
    await cmd('M', addForce + ' 2 0');
    await cmd('M', raycast + ' 3 1');
    await cmd('M', lookup + ' 1 0');
    await cmd('L', 'https://world.example/space');
    await cmd('N');
    const v = (x, y, z) => new BS.Vector3(x, y, z);
    const a = new BS.Unity.Rigidbody(1001).AddForce(v(0, 5, 0), BS.ForceMode.Impulse);
    const b = BS.Unity.Physics.RaycastAll(v(0, 1, 0), v(0, 0, 1), 50);
    const c = BS.GameObject.prototype.GetComponentAsync.call({ Async: async () => {}, unityId: 1001 }, BS.Unity.Rigidbody);
    await pump();
    const [ra, rb, rc] = await Promise.all([a, b, c]);
    check('built bundle: BS.Unity reaches a thunk by its generated id and the promise settles from the reply', () => {
        assert.equal(ra, 2);
        assert.equal(rb, 3);
        assert.equal(rc, 1);
        assert.deepEqual(called.map((x) => x.member), [addForce, raycast, lookup]);
        assert.equal(called[0].args[1], 'i:1');
        assert.equal(called[2].args[0], 'i:' + BS.Unity.Rigidbody.typeId);
    });
    check('built bundle: window.BS keeps its names and gains Unity and BridgeError', () => {
        assert.equal(typeof BS.Unity.Physics.RaycastAll, 'function');
        assert.equal(typeof BS.BridgeError, 'function');
        assert.equal(typeof BS.GameObject, 'function');
        assert.notEqual(BS.Unity.GameObject, BS.GameObject);
    });
    await quit();
}

/** M3: the TS page's ACCEPT, its item numbering and its read-your-writes filter against the C# view's. */
async function pushedFamilies() {
    const B = createRequire(import.meta.url)(join(tmp, 'ts', 'Injection~', 'src', 'bridge', 'index.js'));
    const F = B.Family;
    const peer = startPeer(false, { BRIDGE_PEER_FAMILIES: String(F.FENCE | F.DELTA | F.ANNOUNCE | F.DESTROYED) });
    const { cmd, status } = peer;
    await cmd('L', 'https://w.example/a');
    await cmd('N');
    const host = new Host();
    const page = new B.PageSession(host, () => {}, { href: 'https://w.example/a', isTop: true, sdkVersion: 'interop', docToken: 'tok-m3', heartbeatMs: 0 });
    const deltas = [];
    const destroyed = [];
    page.onPush(F.DELTA, (d) => deltas.push(d));
    page.onPush(F.DESTROYED, (x) => destroyed.push(x));
    const pump = async () => {
        for (let i = 0; i < 40; i++) {
            host.run();
            const up = host.out; host.out = [];
            for (const d of up) await cmd('R', d);
            const down = await cmd('D');
            for (const d of down) page.receive(d);
            host.run();
            if (up.length === 0 && down.length === 0 && host.out.length === 0) return;
        }
    };
    const z = async () => { await cmd('Z'); return status[status.length - 1]; };
    page.sendText('!sst!');
    page.sendText('two');
    page.request(0x80, new Uint8Array([1]), () => {});
    await pump();
    const s1 = await z();
    check('M3: the TS page ACCEPTs what it reads of what C# offers; both number its items the same', () => {
        assert.equal(page.families, F.FENCE | F.DELTA | F.ANNOUNCE | F.DESTROYED);
        assert.equal(page.accepted, F.DELTA | F.DESTROYED);
        assert.equal(s1.accepted, F.DELTA | F.DESTROYED);
        assert.equal(s1.itemsIn, page.itemsSent);
        assert.equal(s1.itemsIn, 3);
        assert.equal(s1.applied, 3);
    });
    page.sendText('!i!:10000¶!cu!¶the write');
    page.noteComponentWrite(6, [8]);
    // Unity reads the property before the write reaches it: the value is older than the write.
    await cmd('P', [5, 6, 7, 8, '8§3§old']);
    await cmd('P', [5, 6, 7, 9, '9§3§other']);
    for (const d of await cmd('D')) page.receive(d);
    host.run();
    await pump();
    await cmd('P', [5, 6, 7, 8, '8§3§new']);
    await cmd('K', '5');
    await pump();
    const s2 = await z();
    check('M3: a value C# read before the TS page\'s write is left out; one read after it, and DESTROYED, arrive', () => {
        assert.deepEqual(deltas.map((d) => d.entries.map((e) => e.text)), [['9§3§other'], ['8§3§new']]);
        assert.equal(deltas[0].applied, 3);
        assert.equal(deltas[1].applied, 4);
        assert.equal(page.stats.staleValues, 1);
        assert.equal(page.pendingWriteCount, 0);
        assert.deepEqual(destroyed, [[{ handle: 5, reason: 1 }]]);
        assert.equal(s2.itemsIn, page.itemsSent);
    });
    await peer.quit();
}

/** M3 in the BUILT bundle: scene.ts reads ANNOUNCE, DELTA and DESTROYED from the C# view into window.BS's mirror. */
async function e2ePushes() {
    const { createPageContext } = await import(pathToFileURL(join(injectionDir, 'test/surface/load-bundle.mjs')).href);
    const F = { FENCE: 2, DELTA: 4, ANNOUNCE: 8, DESTROYED: 16 };
    const { cmd, status, quit } = startPeer(false, { BRIDGE_PEER_FAMILIES: String(F.FENCE | F.DELTA | F.ANNOUNCE | F.DESTROYED) });
    const { context } = createPageContext();
    const sent = [];
    const timeouts = [];
    const oraListeners = [];
    let rnd = 11;
    context.crypto = { getRandomValues: (b) => { for (let i = 0; i < b.length; i++) { rnd = (rnd * 1103515245 + 12345) >>> 0; b[i] = rnd >>> 24; } return b; }, randomUUID: () => '00000000-0000-4000-8000-000000000002' };
    context.setTimeout = (fn, ms) => { timeouts.push({ fn, ms: ms || 0 }); return timeouts.length; };
    context.O = { sendMessage: (type, data) => { sent.push(String(data)); }, messages: { On: (n, fn) => oraListeners.push(fn) } };
    context.location.href = 'https://world.example/space';
    vm.runInContext(readFileSync(bundlePath, 'utf8'), context, { filename: 'injection.js' });
    const BS = context.BS;
    const runZero = () => { for (let g = 0; g < 1000; g++) { const i = timeouts.findIndex((t) => t.ms <= 0); if (i < 0) return; timeouts.splice(i, 1)[0].fn(); } };
    async function pump(rounds = 30) {
        for (let r = 0; r < rounds; r++) {
            runZero();
            const up = sent.splice(0);
            for (const d of up) await cmd('R', d);
            const down = await cmd('D');
            for (const d of down) for (const l of oraListeners.slice()) l({ detail: { id: '', type: 'default-message', data: d } });
            await new Promise((r2) => setImmediate(r2));
            runZero();
            if (up.length === 0 && down.length === 0 && sent.length === 0) return;
        }
    }
    await cmd('L', 'https://world.example/space');
    await cmd('N');
    const scene = BS.Scene.GetInstance();
    await pump();
    await cmd('Z');
    const z = status[status.length - 1];
    // Rigidbody (component type 46), mass (property 186), position (property 1): the lock's ids.
    await cmd('J', [1, 41, 1, 'Cube', 3, 'Untagged', 0]);
    await cmd('J', [2, 41, 42, 46]);
    await pump();
    const announced = scene.objects[41];
    const rb = scene.components[42];
    await cmd('P', [41, 42, 46, 186, '186§5§2.5']);
    await cmd('P', [41, 0, 0, 1, '1|1|2|3']);
    let watched = 0;
    if (announced) announced.watchCallbackFunction = () => { watched++; };
    await pump();
    const events = [];
    if (announced) announced.On('destroyed', (e) => events.push('destroyed ' + e.detail.reason));
    scene.On('object-removed', (e) => events.push('object-removed ' + e.detail.unityId));
    await cmd('K', '41');
    await pump();
    check('built bundle: scene.ts ACCEPTs, makes announced objects and components, applies DELTA, and DESTROYED fires its events', () => {
        assert.equal(z.accepted, F.DELTA | F.ANNOUNCE | F.DESTROYED);
        assert.ok(announced, 'announced object in scene.objects');
        assert.equal(announced.name, 'Cube');
        assert.equal(announced.layer, 3);
        assert.equal(announced.active, true);
        assert.ok(rb, 'announced component in scene.components');
        assert.equal(rb.mass, 2.5);
        assert.equal(announced.transform.position.x, 1);
        assert.equal(announced.transform.position.z, 3);
        assert.equal(watched, 1, 'the transform watcher called once');
        assert.deepEqual(events, ['destroyed destroyed', 'object-removed 41']);
        assert.equal(scene.objects[41], undefined);
        assert.equal(scene.components[42], undefined);
    });
    await quit();
}

let code = 0;
try {
    build();
    await interop();
    await e2eBundle();
    await typedCalls();
    await e2eUnity();
    await pushedFamilies();
    await e2ePushes();
    console.log(checks + ' interop checks passed');
} catch (e) {
    console.error('INTEROP FAILED:', e);
    code = 1;
} finally {
    for (const p of peers) if (p.proc.exitCode === null) p.proc.kill();
    await Promise.all(peers.map((p) => p.exited));
    try { rmSync(tmp, { recursive: true, force: true }); } catch (e) { console.warn('could not remove ' + tmp + ': ' + e.message); }
}
process.exit(code);
