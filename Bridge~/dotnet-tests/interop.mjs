// Cross-language checks against the real C# BridgeView, driven through this suite's --peer mode (Peer.cs):
//   1. interop: the real TS PageSession (compiled from Injection~/src/bridge) <-> BridgeView;
//   2. e2e: the BUILT injection.js, in the M0 page shim <-> BridgeView.
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
function startPeer() {
    const proc = spawn('dotnet', [join(tmp, 'dotnet', 'BridgeCoreTests.dll'), '--peer'], { stdio: ['pipe', 'pipe', 'inherit'] });
    const exited = new Promise((r) => proc.on('exit', r));
    peers.push({ proc, exited });
    let buf = '';
    const lines = [];
    const ran = [];
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
        }
        return out;
    }
    return { cmd, ran, quit: () => { proc.stdin.end('Q\n'); return exited; } };
}

let checks = 0;
function check(name, fn) { fn(); checks++; console.log('  ok   ' + name); }

async function interop() {
    const B = createRequire(import.meta.url)(join(tmp, 'ts', 'Injection~', 'src', 'bridge', 'index.js'));
    const { cmd, ran, quit } = startPeer();
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

let code = 0;
try {
    build();
    await interop();
    await e2eBundle();
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
