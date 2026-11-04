// Cross-language checks against the real C# BridgeView, driven through this suite's --peer mode (Peer.cs):
//   1. interop: the real TS PageSession (compiled from Injection~/src/bridge) <-> BridgeView;
//   2. e2e: the BUILT injection.js, with the session prelude, in the M0 page shim <-> BridgeView.
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
        constructor() { this.out = []; this.tasks = []; this.t = 0; }
        post(d) { this.out.push(d); }
        now() { return this.t; }
        schedule(fn) { this.tasks.push(fn); }
        setTimer() { return 0; }
        clearTimer() {}
        run() { while (this.tasks.length) this.tasks.shift()(); }
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
    context.setInterval = () => { throw new Error('the session lane must not start an interval timer'); };
    context.O = { sendMessage: (type, data) => { assert.equal(type, 'custom-message'); sent.push(String(data)); }, messages: { On: (n, fn) => oraListeners.push(fn) } };
    context.__BS_BRIDGE_CONFIG = { mode: 'session' };
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
    check('built bundle (session prelude) <-> C# view: handshake, 3 ops in epoch 1, GetPlatform answered in a frame', () => {
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
