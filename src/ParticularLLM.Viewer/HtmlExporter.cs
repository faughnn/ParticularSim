using System.IO.Compression;
using System.Text;
using ParticularLLM.Viewer.Scenarios;

namespace ParticularLLM.Viewer;

public static class HtmlExporter
{
    public static void Export(List<ScenarioDef> scenarios, string outputPath)
    {
        var sb = new StringBuilder();

        // Pre-simulate all scenarios and collect data
        var scenarioDataList = new List<ScenarioData>();
        for (int i = 0; i < scenarios.Count; i++)
        {
            var def = scenarios[i];
            Console.Write($"  Simulating [{i + 1}/{scenarios.Count}] {def.Name}...");
            var data = PreSimulate(def);
            scenarioDataList.Add(data);
            Console.WriteLine($" {data.FrameCount} frames, {data.CompressedBase64.Length / 1024}KB compressed");
        }

        // Build color table from materials
        var colorTable = BuildColorTable();

        // Generate HTML
        sb.Append(HtmlHead());
        sb.Append(HtmlStyles());
        sb.Append(HtmlBody());
        sb.Append(ScriptOpen());
        sb.Append(EmbedColorTable(colorTable));
        sb.Append(EmbedScenarioData(scenarioDataList));
        sb.Append(PlayerScript());
        sb.Append(ScriptClose());
        sb.Append(HtmlFooter());

        File.WriteAllText(outputPath, sb.ToString());
    }

    private static ScenarioData PreSimulate(ScenarioDef def)
    {
        using var fixture = new ViewerFixture(def.Width, def.Height);
        def.Setup(fixture);

        int cellCount = def.Width * def.Height;
        int totalFrames = def.SuggestedFrames + 1; // +1 for initial state
        var frames = new byte[totalFrames * cellCount];

        // Capture frame 0 (initial state)
        for (int j = 0; j < cellCount; j++)
            frames[j] = fixture.World.cells[j].materialId;

        // Simulate and capture each frame
        for (int f = 1; f < totalFrames; f++)
        {
            fixture.Step();
            int offset = f * cellCount;
            for (int j = 0; j < cellCount; j++)
                frames[offset + j] = fixture.World.cells[j].materialId;
        }

        // GZip compress
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            gz.Write(frames, 0, frames.Length);
        }
        string compressed = Convert.ToBase64String(ms.ToArray());

        return new ScenarioData(
            def.Name, def.Category, def.Description,
            def.Width, def.Height, totalFrames, compressed
        );
    }

    private record ScenarioData(
        string Name, string Category, string Description,
        int Width, int Height, int FrameCount, string CompressedBase64
    );

    private static List<(byte r, byte g, byte b, string name)> BuildColorTable()
    {
        var world = new CellWorld(1, 1);
        var table = new List<(byte r, byte g, byte b, string name)>();
        for (int i = 0; i < 25; i++)
        {
            var c = world.materials[i].baseColour;
            string name = MaterialName((byte)i);
            table.Add((c.r, c.g, c.b, name));
        }
        world.Dispose();
        return table;
    }

    private static string MaterialName(byte mat) => mat switch
    {
        Materials.Air => "Air",
        Materials.Stone => "Stone",
        Materials.Sand => "Sand",
        Materials.Water => "Water",
        Materials.Oil => "Oil",
        Materials.Steam => "Steam",
        Materials.IronOre => "Iron Ore",
        Materials.MoltenIron => "Molten Iron",
        Materials.Iron => "Iron",
        Materials.Coal => "Coal",
        Materials.Ash => "Ash",
        Materials.Smoke => "Smoke",
        Materials.Belt => "Belt",
        Materials.BeltLeft => "Belt",
        Materials.BeltRight => "Belt",
        Materials.BeltLeftLight => "Belt",
        Materials.BeltRightLight => "Belt",
        Materials.Dirt => "Dirt",
        Materials.Ground => "Ground",
        Materials.LiftUp => "Lift",
        Materials.LiftUpLight => "Lift",
        Materials.Wall => "Wall",
        Materials.PistonBase => "Piston Base",
        Materials.PistonArm => "Piston Arm",
        Materials.Furnace => "Furnace",
        _ => $"Mat#{mat}",
    };

    // --- HTML generation methods ---

    private static string HtmlHead() => """
    <!DOCTYPE html>
    <html lang="en">
    <head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>ParticularLLM Simulation Viewer</title>
    """;

    private static string HtmlStyles() => """
    <style>
    * { margin: 0; padding: 0; box-sizing: border-box; }
    body { background: #1a1a2e; color: #e0e0e0; font-family: 'Segoe UI', system-ui, sans-serif; display: flex; height: 100vh; overflow: hidden; }

    /* Sidebar */
    #sidebar { width: 280px; min-width: 280px; background: #16213e; border-right: 1px solid #334; display: flex; flex-direction: column; height: 100vh; }
    #sidebar h2 { padding: 16px; font-size: 14px; color: #8892b0; text-transform: uppercase; letter-spacing: 1px; border-bottom: 1px solid #334; }
    #scenario-list { flex: 1; overflow-y: auto; }
    .scenario-group { padding: 8px 0; }
    .scenario-group-label { padding: 4px 16px; font-size: 11px; color: #5a6785; text-transform: uppercase; letter-spacing: 1px; }
    .scenario-item { padding: 8px 16px; cursor: pointer; display: flex; align-items: center; gap: 8px; font-size: 13px; transition: background 0.15s; }
    .scenario-item:hover { background: #1a2744; }
    .scenario-item.active { background: #1f3460; color: #fff; }
    .scenario-item .dot { width: 8px; height: 8px; border-radius: 50%; flex-shrink: 0; }
    .dot.unreviewed { background: #555; }
    .dot.pass { background: #4caf50; }
    .dot.fail { background: #f44336; }

    /* Progress bar */
    #progress-bar { padding: 12px 16px; border-top: 1px solid #334; font-size: 12px; color: #8892b0; }
    #progress-bar .bar { height: 4px; background: #334; border-radius: 2px; margin-top: 6px; overflow: hidden; }
    #progress-bar .bar-fill { height: 100%; background: linear-gradient(90deg, #4caf50, #8bc34a); transition: width 0.3s; }

    /* Main area */
    #main { flex: 1; display: flex; flex-direction: column; overflow: hidden; }

    /* Header */
    #header { padding: 12px 20px; border-bottom: 1px solid #334; display: flex; align-items: center; gap: 16px; }
    #header h1 { font-size: 18px; font-weight: 600; flex: 1; }
    #header .desc { font-size: 13px; color: #8892b0; flex: 2; }

    /* Canvas area */
    #canvas-area { flex: 1; display: flex; justify-content: center; align-items: center; padding: 16px; overflow: hidden; position: relative; }
    canvas { image-rendering: pixelated; image-rendering: crisp-edges; background: #111; }

    /* Controls bar */
    #controls { padding: 12px 20px; border-top: 1px solid #334; display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    #controls button { background: #2a3a5c; border: 1px solid #445; color: #e0e0e0; padding: 6px 14px; border-radius: 4px; cursor: pointer; font-size: 13px; transition: background 0.15s; }
    #controls button:hover { background: #3a4a6c; }
    #controls button.active { background: #4a5a7c; }
    #controls .separator { width: 1px; height: 24px; background: #445; }
    #frame-display { font-size: 13px; color: #8892b0; min-width: 120px; }
    #speed-display { font-size: 13px; color: #8892b0; min-width: 60px; }
    input[type=range] { width: 100px; accent-color: #5a7abf; }

    /* Review bar */
    #review-bar { padding: 12px 20px; border-top: 1px solid #334; display: flex; align-items: flex-start; gap: 12px; }
    #review-buttons { display: flex; gap: 8px; flex-shrink: 0; }
    #review-buttons button { padding: 8px 20px; border-radius: 4px; border: 2px solid transparent; font-weight: 600; font-size: 13px; cursor: pointer; transition: all 0.15s; }
    #btn-pass { background: #1b3a1b; color: #4caf50; border-color: #2d5a2d; }
    #btn-pass:hover, #btn-pass.selected { background: #2d5a2d; color: #6fcf6f; }
    #btn-fail { background: #3a1b1b; color: #f44336; border-color: #5a2d2d; }
    #btn-fail:hover, #btn-fail.selected { background: #5a2d2d; color: #ff6f6f; }
    #btn-skip { background: #2a2a2a; color: #888; border-color: #444; }
    #btn-skip:hover, #btn-skip.selected { background: #3a3a3a; color: #aaa; }
    #notes-area { flex: 1; display: flex; flex-direction: column; gap: 4px; }
    #notes-area textarea { background: #111; border: 1px solid #445; color: #e0e0e0; border-radius: 4px; padding: 8px; font-size: 13px; font-family: inherit; resize: vertical; min-height: 36px; max-height: 120px; }
    #notes-area label { font-size: 11px; color: #667; }

    /* Legend */
    #legend { display: flex; gap: 12px; padding: 8px 20px; border-top: 1px solid #334; flex-wrap: wrap; font-size: 12px; }
    .legend-item { display: flex; align-items: center; gap: 4px; }
    .legend-swatch { width: 12px; height: 12px; border-radius: 2px; }
    .legend-count { color: #8892b0; }

    /* Export/import buttons */
    #review-actions { padding: 8px 16px; border-top: 1px solid #334; display: flex; gap: 8px; }
    #review-actions button { background: #2a3a5c; border: 1px solid #445; color: #c0c0c0; padding: 5px 12px; border-radius: 4px; cursor: pointer; font-size: 12px; }
    #review-actions button:hover { background: #3a4a6c; }

    /* Keyboard hints */
    kbd { background: #2a2a3a; border: 1px solid #445; border-radius: 3px; padding: 1px 5px; font-size: 11px; font-family: inherit; }
    </style>
    """;

    private static string HtmlBody() => """
    </head>
    <body>
    <div id="sidebar">
      <h2>Scenarios</h2>
      <div id="scenario-list"></div>
      <div id="review-actions">
        <button onclick="exportReview()" title="Download review as JSON">Export Review</button>
        <button onclick="document.getElementById('import-file').click()" title="Import previous review JSON">Import</button>
        <input type="file" id="import-file" accept=".json" style="display:none" onchange="importReview(event)">
        <button onclick="clearReview()" title="Reset all review data" style="margin-left:auto; color:#f44336;">Clear All</button>
      </div>
      <div id="progress-bar">
        <span id="progress-text">0/0 reviewed</span>
        <div class="bar"><div class="bar-fill" id="progress-fill" style="width:0%"></div></div>
      </div>
    </div>
    <div id="main">
      <div id="header">
        <h1 id="scenario-title">Loading...</h1>
        <span class="desc" id="scenario-desc"></span>
      </div>
      <div id="canvas-area">
        <canvas id="canvas"></canvas>
      </div>
      <div id="legend"></div>
      <div id="controls">
        <button id="btn-play" onclick="togglePlay()" title="Space">&#9654;</button>
        <button onclick="stepBack()" title="Left arrow">&#9664;&#9664;</button>
        <button onclick="stepForward()" title="Right arrow / .">&#9654;&#9654;</button>
        <button onclick="restart()" title="R">&#8634;</button>
        <div class="separator"></div>
        <span id="frame-display">Frame 0 / 0</span>
        <div class="separator"></div>
        <label style="font-size:12px;color:#889">Speed:</label>
        <input type="range" id="speed-slider" min="0" max="7" value="3" oninput="updateSpeed()">
        <span id="speed-display">10 fps</span>
        <div class="separator"></div>
        <span style="font-size:11px;color:#556">
          <kbd>Space</kbd> play &nbsp; <kbd>.</kbd> step &nbsp; <kbd>&larr;</kbd><kbd>&rarr;</kbd> frame &nbsp; <kbd>&lt;</kbd><kbd>&gt;</kbd> speed &nbsp; <kbd>R</kbd> restart
        </span>
      </div>
      <div id="review-bar">
        <div id="review-buttons">
          <button id="btn-pass" onclick="setReview('pass')">&#10004; Pass</button>
          <button id="btn-fail" onclick="setReview('fail')">&#10008; Fail</button>
          <button id="btn-skip" onclick="setReview('unreviewed')">Skip</button>
        </div>
        <div id="notes-area">
          <label>Notes (optional)</label>
          <textarea id="notes-input" placeholder="Describe what looks wrong..." oninput="saveNotes()"></textarea>
        </div>
      </div>
    </div>
    </body>
    """;

    private static string ScriptOpen() => "\n<script>\n";
    private static string ScriptClose() => "\n</script>\n</html>\n";

    private static string EmbedColorTable(List<(byte r, byte g, byte b, string name)> table)
    {
        var sb = new StringBuilder();
        sb.Append("const COLORS = [");
        for (int i = 0; i < table.Count; i++)
        {
            var (r, g, b, _) = table[i];
            sb.Append($"[{r},{g},{b}]");
            if (i < table.Count - 1) sb.Append(',');
        }
        sb.AppendLine("];");

        sb.Append("const MAT_NAMES = [");
        for (int i = 0; i < table.Count; i++)
        {
            sb.Append($"\"{EscapeJs(table[i].name)}\"");
            if (i < table.Count - 1) sb.Append(',');
        }
        sb.AppendLine("];");

        return sb.ToString();
    }

    private static string EmbedScenarioData(List<ScenarioData> scenarios)
    {
        var sb = new StringBuilder();
        sb.AppendLine("const SCENARIOS = [");
        for (int i = 0; i < scenarios.Count; i++)
        {
            var s = scenarios[i];
            sb.Append("  {");
            sb.Append($"name:\"{EscapeJs(s.Name)}\",");
            sb.Append($"category:\"{EscapeJs(s.Category)}\",");
            sb.Append($"desc:\"{EscapeJs(s.Description)}\",");
            sb.Append($"w:{s.Width},h:{s.Height},frames:{s.FrameCount},");
            sb.Append($"data:\"");
            sb.Append(s.CompressedBase64);
            sb.Append("\"}");
            if (i < scenarios.Count - 1) sb.Append(',');
            sb.AppendLine();
        }
        sb.AppendLine("];");
        return sb.ToString();
    }

    private static string EscapeJs(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    private static string PlayerScript() => """
    // --- State ---
    let currentIdx = 0;
    let frameData = null; // Uint8Array of all frames
    let currentFrame = 0;
    let playing = false;
    let lastFrameTime = 0;
    const FPS_STEPS = [1, 2, 5, 10, 15, 20, 30, 60];
    let fpsIndex = 3;
    let pixelSize = 8;

    const canvas = document.getElementById('canvas');
    const ctx = canvas.getContext('2d');
    let imageData = null;

    // --- Review state (localStorage) ---
    const STORAGE_KEY = 'particularllm-review';
    function loadReviewState() {
      try { return JSON.parse(localStorage.getItem(STORAGE_KEY)) || {}; } catch { return {}; }
    }
    function saveReviewState(state) {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
    }
    let reviewState = loadReviewState();

    // --- Build sidebar ---
    function buildSidebar() {
      const list = document.getElementById('scenario-list');
      list.innerHTML = '';
      let lastCat = null;
      SCENARIOS.forEach((s, i) => {
        if (s.category !== lastCat) {
          lastCat = s.category;
          const g = document.createElement('div');
          g.className = 'scenario-group-label';
          g.textContent = s.category;
          list.appendChild(g);
        }
        const item = document.createElement('div');
        item.className = 'scenario-item' + (i === currentIdx ? ' active' : '');
        item.dataset.index = i;
        const status = (reviewState[s.name] || {}).status || 'unreviewed';
        item.innerHTML = `<span class="dot ${status}"></span><span>${s.name}</span>`;
        item.onclick = () => loadScenario(i);
        list.appendChild(item);
      });
      updateProgress();
    }

    function updateProgress() {
      const total = SCENARIOS.length;
      let reviewed = 0;
      SCENARIOS.forEach(s => {
        const st = (reviewState[s.name] || {}).status;
        if (st === 'pass' || st === 'fail') reviewed++;
      });
      document.getElementById('progress-text').textContent = `${reviewed}/${total} reviewed`;
      document.getElementById('progress-fill').style.width = `${(reviewed / total * 100).toFixed(1)}%`;
    }

    // --- Decompress scenario data ---
    async function decompress(base64) {
      const binary = atob(base64);
      const bytes = new Uint8Array(binary.length);
      for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
      const ds = new DecompressionStream('gzip');
      const reader = new Blob([bytes]).stream().pipeThrough(ds).getReader();
      const chunks = [];
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        chunks.push(value);
      }
      const totalLen = chunks.reduce((a, c) => a + c.length, 0);
      const result = new Uint8Array(totalLen);
      let offset = 0;
      for (const c of chunks) { result.set(c, offset); offset += c.length; }
      return result;
    }

    // --- Load scenario ---
    async function loadScenario(idx) {
      currentIdx = idx;
      const s = SCENARIOS[idx];
      document.getElementById('scenario-title').textContent = s.name;
      document.getElementById('scenario-desc').textContent = s.desc;

      // Size canvas
      const area = document.getElementById('canvas-area');
      const maxW = area.clientWidth - 32;
      const maxH = area.clientHeight - 32;
      pixelSize = Math.max(1, Math.min(Math.floor(maxW / s.w), Math.floor(maxH / s.h)));
      canvas.width = s.w * pixelSize;
      canvas.height = s.h * pixelSize;
      ctx.imageSmoothingEnabled = false;

      // Small offscreen canvas for pixel data
      imageData = ctx.createImageData(s.w, s.h);

      // Decompress
      document.getElementById('scenario-title').textContent = s.name + ' (loading...)';
      frameData = await decompress(s.data);
      document.getElementById('scenario-title').textContent = s.name;

      currentFrame = 0;
      playing = true;
      lastFrameTime = performance.now();
      document.getElementById('btn-play').innerHTML = '&#9646;&#9646;';
      renderFrame();
      updateReviewUI();
      buildSidebar();
    }

    // --- Render ---
    function renderFrame() {
      const s = SCENARIOS[currentIdx];
      const cellCount = s.w * s.h;
      const offset = currentFrame * cellCount;
      const pixels = imageData.data;

      // Count materials for legend
      const counts = new Map();
      for (let i = 0; i < cellCount; i++) {
        const matId = frameData[offset + i];
        const ci = matId < COLORS.length ? matId : 0;
        const pi = i * 4;
        pixels[pi]     = COLORS[ci][0];
        pixels[pi + 1] = COLORS[ci][1];
        pixels[pi + 2] = COLORS[ci][2];
        pixels[pi + 3] = 255;
        if (matId !== 0) counts.set(matId, (counts.get(matId) || 0) + 1);
      }

      // Draw to small imageData then scale up
      const offscreen = new OffscreenCanvas(s.w, s.h);
      const offCtx = offscreen.getContext('2d');
      offCtx.putImageData(imageData, 0, 0);
      ctx.imageSmoothingEnabled = false;
      ctx.drawImage(offscreen, 0, 0, canvas.width, canvas.height);

      // Frame display
      document.getElementById('frame-display').textContent = `Frame ${currentFrame} / ${s.frames - 1}`;

      // Legend
      const legend = document.getElementById('legend');
      let legendHtml = '';
      const sorted = [...counts.entries()].sort((a, b) => b[1] - a[1]);
      for (const [matId, count] of sorted) {
        const ci = matId < COLORS.length ? matId : 0;
        const name = matId < MAT_NAMES.length ? MAT_NAMES[matId] : `#${matId}`;
        legendHtml += `<span class="legend-item"><span class="legend-swatch" style="background:rgb(${COLORS[ci]})"></span>${name} <span class="legend-count">x${count}</span></span>`;
      }
      legend.innerHTML = legendHtml;
    }

    // --- Playback ---
    function togglePlay() {
      playing = !playing;
      document.getElementById('btn-play').innerHTML = playing ? '&#9646;&#9646;' : '&#9654;';
      if (playing) lastFrameTime = performance.now();
    }

    function stepForward() {
      const s = SCENARIOS[currentIdx];
      if (currentFrame < s.frames - 1) { currentFrame++; renderFrame(); }
    }

    function stepBack() {
      if (currentFrame > 0) { currentFrame--; renderFrame(); }
    }

    function restart() {
      currentFrame = 0;
      renderFrame();
    }

    function updateSpeed() {
      fpsIndex = parseInt(document.getElementById('speed-slider').value);
      document.getElementById('speed-display').textContent = `${FPS_STEPS[fpsIndex]} fps`;
    }

    function animationLoop(timestamp) {
      if (playing && frameData) {
        const fps = FPS_STEPS[fpsIndex];
        const interval = 1000 / fps;
        if (timestamp - lastFrameTime >= interval) {
          lastFrameTime = timestamp - ((timestamp - lastFrameTime) % interval);
          const s = SCENARIOS[currentIdx];
          if (currentFrame < s.frames - 1) {
            currentFrame++;
            renderFrame();
          } else {
            playing = false;
            document.getElementById('btn-play').innerHTML = '&#9654;';
          }
        }
      }
      requestAnimationFrame(animationLoop);
    }
    requestAnimationFrame(animationLoop);

    // --- Review controls ---
    function setReview(status) {
      const name = SCENARIOS[currentIdx].name;
      if (!reviewState[name]) reviewState[name] = {};
      reviewState[name].status = status;
      if (status === 'fail' && !reviewState[name].notes) {
        reviewState[name].failFrame = currentFrame;
      }
      saveReviewState(reviewState);
      updateReviewUI();
      buildSidebar();
      // Auto-advance on pass or fail
      if ((status === 'pass' || status === 'fail') && currentIdx < SCENARIOS.length - 1) {
        loadScenario(currentIdx + 1);
      }
    }

    function saveNotes() {
      const name = SCENARIOS[currentIdx].name;
      if (!reviewState[name]) reviewState[name] = { status: 'unreviewed' };
      reviewState[name].notes = document.getElementById('notes-input').value;
      saveReviewState(reviewState);
    }

    function updateReviewUI() {
      const name = SCENARIOS[currentIdx].name;
      const r = reviewState[name] || {};
      const status = r.status || 'unreviewed';
      document.getElementById('btn-pass').className = status === 'pass' ? 'selected' : '';
      document.getElementById('btn-fail').className = status === 'fail' ? 'selected' : '';
      document.getElementById('btn-skip').className = status === 'unreviewed' ? 'selected' : '';
      document.getElementById('notes-input').value = r.notes || '';
    }

    // --- Export / Import ---
    function exportReview() {
      const report = SCENARIOS.map(s => {
        const r = reviewState[s.name] || {};
        return { name: s.name, category: s.category, status: r.status || 'unreviewed', notes: r.notes || '', failFrame: r.failFrame };
      });
      const blob = new Blob([JSON.stringify(report, null, 2)], { type: 'application/json' });
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = `review-${new Date().toISOString().slice(0, 10)}.json`;
      a.click();
    }

    function importReview(event) {
      const file = event.target.files[0];
      if (!file) return;
      const reader = new FileReader();
      reader.onload = (e) => {
        try {
          const data = JSON.parse(e.target.result);
          data.forEach(item => {
            reviewState[item.name] = { status: item.status, notes: item.notes || '', failFrame: item.failFrame };
          });
          saveReviewState(reviewState);
          buildSidebar();
          updateReviewUI();
        } catch (err) { alert('Invalid review JSON: ' + err.message); }
      };
      reader.readAsText(file);
      event.target.value = '';
    }

    function clearReview() {
      if (!confirm('Clear all review data?')) return;
      reviewState = {};
      saveReviewState(reviewState);
      buildSidebar();
      updateReviewUI();
    }

    // --- Keyboard shortcuts ---
    document.addEventListener('keydown', (e) => {
      if (e.target.tagName === 'TEXTAREA' || e.target.tagName === 'INPUT') return;
      switch (e.key) {
        case ' ': e.preventDefault(); togglePlay(); break;
        case '.': stepForward(); break;
        case 'ArrowRight': stepForward(); break;
        case 'ArrowLeft': stepBack(); break;
        case '<': case ',':
          fpsIndex = Math.max(0, fpsIndex - 1);
          document.getElementById('speed-slider').value = fpsIndex;
          updateSpeed();
          break;
        case '>':
          fpsIndex = Math.min(FPS_STEPS.length - 1, fpsIndex + 1);
          document.getElementById('speed-slider').value = fpsIndex;
          updateSpeed();
          break;
        case 'r': case 'R': restart(); break;
        case 'n': case 'N': loadScenario((currentIdx + 1) % SCENARIOS.length); break;
        case 'p': case 'P': loadScenario((currentIdx - 1 + SCENARIOS.length) % SCENARIOS.length); break;
      }
    });

    // --- Init ---
    updateSpeed();
    buildSidebar();
    loadScenario(0);
    """;

    private static string HtmlFooter() => "";
}
