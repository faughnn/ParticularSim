using System.Text;

namespace ParticularLLM.Viewer;

public static class HtmlExporter
{
    public static void Export(List<ScenarioData> captured, string outputPath, ReviewState reviewState)
    {
        var sb = new StringBuilder();
        var colorTable = BuildColorTable();

        sb.Append(HtmlHead());
        sb.Append(HtmlStyles());
        sb.Append(HtmlBody());
        sb.Append(ScriptOpen());
        sb.Append(EmbedColorTable(colorTable));
        sb.Append(EmbedScenarioData(captured));
        sb.Append(EmbedReviewState(reviewState));
        sb.Append(PlayerScript());
        sb.Append(ScriptClose());
        sb.Append(HtmlFooter());

        File.WriteAllText(outputPath, sb.ToString());
    }

    public record FurnaceBlockInfo(int GridX, int GridY, byte Direction);

    public record ScenarioData(
        string Name, string Category, string Description,
        int Width, int Height, int FrameCount, string CompressedBase64,
        string[] Tags,
        bool HasTemperature = false,
        FurnaceBlockInfo[]? FurnaceBlocks = null
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

    /* Filter bar */
    #filter-bar { display: flex; gap: 4px; padding: 8px 16px; border-bottom: 1px solid #334; }
    #filter-bar button { background: #2a3a5c; border: 1px solid #445; color: #8892b0; padding: 4px 10px; border-radius: 3px; cursor: pointer; font-size: 11px; transition: background 0.15s; }
    #filter-bar button:hover { background: #3a4a6c; }
    #filter-bar button.active { background: #4a5a7c; color: #fff; }
    #btn-next-queued { margin-left: auto; }

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

    /* Settings panel */
    #settings-wrap { position: relative; }
    #settings-btn { font-size: 16px; line-height: 1; padding: 6px 10px; }
    #settings-panel { display: none; position: absolute; bottom: 100%; right: 0; background: #1e2a4a; border: 1px solid #445; border-radius: 6px; padding: 12px 16px; min-width: 200px; box-shadow: 0 4px 16px rgba(0,0,0,.4); z-index: 10; }
    #settings-panel.open { display: block; }
    #settings-panel h3 { font-size: 12px; color: #8892b0; text-transform: uppercase; letter-spacing: 1px; margin-bottom: 10px; }
    .setting-row { display: flex; align-items: center; justify-content: space-between; padding: 4px 0; font-size: 13px; }
    .setting-row label { cursor: pointer; user-select: none; }
    .toggle { position: relative; width: 36px; height: 20px; background: #334; border-radius: 10px; cursor: pointer; transition: background 0.2s; flex-shrink: 0; }
    .toggle.on { background: #4a7abf; }
    .toggle::after { content: ''; position: absolute; top: 2px; left: 2px; width: 16px; height: 16px; background: #e0e0e0; border-radius: 50%; transition: transform 0.2s; }
    .toggle.on::after { transform: translateX(16px); }
    </style>
    """;

    private static string HtmlBody() => """
    </head>
    <body>
    <div id="sidebar">
      <h2>Scenarios</h2>
      <div id="filter-bar">
        <button id="btn-filter-all" onclick="setFilter('all')">All</button>
        <button id="btn-filter-unreviewed" onclick="setFilter('unreviewed')">Unreviewed</button>
        <button id="btn-filter-fail" onclick="setFilter('fail')">Failed</button>
        <button id="btn-next-queued" onclick="jumpNextUnreviewed()" title="Tab">Next [Tab]</button>
      </div>
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
        <button id="btn-play" onclick="togglePlay()" title="Space">&#9654; [Space]</button>
        <button onclick="stepBack()" title="Left arrow">&#9664;&#9664; [&#8592;]</button>
        <button onclick="stepForward()" title="Right arrow">&#9654;&#9654; [&#8594;]</button>
        <button onclick="restart()" title="R">&#8634; [R]</button>
        <div class="separator"></div>
        <span id="frame-display">Frame 0 / 0</span>
        <div class="separator"></div>
        <label style="font-size:12px;color:#889">Speed:</label>
        <input type="range" id="speed-slider" min="0" max="7" value="3" oninput="updateSpeed()">
        <span id="speed-display">10 fps</span>
        <div style="flex:1"></div>
        <div id="settings-wrap">
          <button id="settings-btn" onclick="toggleSettings()" title="Settings">&#9881;</button>
          <div id="settings-panel">
            <h3>Settings</h3>
            <div class="setting-row">
              <label onclick="toggleSetting('loop')">Loop playback</label>
              <div id="toggle-loop" class="toggle" onclick="toggleSetting('loop')"></div>
            </div>
            <div class="setting-row">
              <label onclick="toggleSetting('autoplay')">Autoplay on load</label>
              <div id="toggle-autoplay" class="toggle on" onclick="toggleSetting('autoplay')"></div>
            </div>
          </div>
        </div>
      </div>
      <div id="review-bar">
        <div id="review-buttons">
          <button id="btn-pass" onclick="setReview('pass')">&#10004; Pass [1]</button>
          <button id="btn-fail" onclick="setReview('fail')">&#10008; Fail [2]</button>
          <button id="btn-skip" onclick="setReview('unreviewed')">Skip [3]</button>
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

    private static string EmbedReviewState(ReviewState state)
    {
        var sb = new StringBuilder();
        sb.Append("const EMBEDDED_REVIEW = {");
        bool first = true;
        foreach (var (name, review) in state.Scenarios)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{EscapeJs(name)}\":{{status:\"{EscapeJs(review.Status)}\",notes:\"{EscapeJs(review.Notes)}\"}}");
        }
        sb.AppendLine("};");
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

    // --- Settings (localStorage) ---
    const SETTINGS_KEY = 'particularllm-settings';
    let settings = (() => {
      try { return JSON.parse(localStorage.getItem(SETTINGS_KEY)) || {}; } catch { return {}; }
    })();
    if (settings.loop === undefined) settings.loop = false;
    if (settings.autoplay === undefined) settings.autoplay = true;
    function saveSettings() { localStorage.setItem(SETTINGS_KEY, JSON.stringify(settings)); }
    function toggleSettings() { document.getElementById('settings-panel').classList.toggle('open'); }
    function toggleSetting(key) {
      settings[key] = !settings[key];
      saveSettings();
      updateSettingsUI();
    }
    function updateSettingsUI() {
      document.getElementById('toggle-loop').className = 'toggle' + (settings.loop ? ' on' : '');
      document.getElementById('toggle-autoplay').className = 'toggle' + (settings.autoplay ? ' on' : '');
    }
    // Close settings when clicking outside
    document.addEventListener('click', (e) => {
      const wrap = document.getElementById('settings-wrap');
      if (!wrap.contains(e.target)) document.getElementById('settings-panel').classList.remove('open');
    });

    // --- Review state (localStorage) ---
    const STORAGE_KEY = 'particularllm-review';
    const FILTER_KEY = 'particularllm-filter';
    function loadReviewState() {
      try { return JSON.parse(localStorage.getItem(STORAGE_KEY)) || {}; } catch { return {}; }
    }
    function saveReviewState(state) {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
    }

    // Merge embedded review state (CLI is authoritative — embedded wins on conflict)
    let reviewState = loadReviewState();
    if (typeof EMBEDDED_REVIEW !== 'undefined') {
      for (const [name, data] of Object.entries(EMBEDDED_REVIEW)) {
        reviewState[name] = { ...reviewState[name], ...data };
      }
      saveReviewState(reviewState);
    }

    // --- Filter state ---
    let currentFilter = localStorage.getItem(FILTER_KEY) || 'auto';
    function getEffectiveFilter() {
      if (currentFilter === 'auto') {
        const hasUnreviewed = SCENARIOS.some(s => {
          const st = (reviewState[s.name] || {}).status;
          return st !== 'pass' && st !== 'fail';
        });
        return hasUnreviewed ? 'unreviewed' : 'all';
      }
      return currentFilter;
    }
    function setFilter(f) {
      currentFilter = f;
      localStorage.setItem(FILTER_KEY, f);
      buildSidebar();
    }
    function matchesFilter(scenarioName, filter) {
      const st = (reviewState[scenarioName] || {}).status || 'unreviewed';
      if (filter === 'all') return true;
      if (filter === 'unreviewed') return st !== 'pass' && st !== 'fail';
      if (filter === 'fail') return st === 'fail';
      return true;
    }
    function isUnreviewed(scenarioName) {
      const st = (reviewState[scenarioName] || {}).status;
      return st !== 'pass' && st !== 'fail';
    }

    // --- Build sidebar ---
    function buildSidebar() {
      const list = document.getElementById('scenario-list');
      list.innerHTML = '';
      const filter = getEffectiveFilter();
      // Update filter button states
      document.getElementById('btn-filter-all').className = filter === 'all' ? 'active' : '';
      document.getElementById('btn-filter-unreviewed').className = filter === 'unreviewed' ? 'active' : '';
      document.getElementById('btn-filter-fail').className = filter === 'fail' ? 'active' : '';

      let lastCat = null;
      SCENARIOS.forEach((s, i) => {
        if (!matchesFilter(s.name, filter)) return;
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
      playing = settings.autoplay;
      lastFrameTime = performance.now();
      document.getElementById('btn-play').innerHTML = playing ? '&#9646;&#9646;' : '&#9654;';
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
      // If at the end, restart and play
      const s = SCENARIOS[currentIdx];
      if (!playing && currentFrame >= s.frames - 1) {
        currentFrame = 0;
        playing = true;
        lastFrameTime = performance.now();
        document.getElementById('btn-play').innerHTML = '&#9646;&#9646;';
        renderFrame();
        return;
      }
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
      playing = true;
      lastFrameTime = performance.now();
      document.getElementById('btn-play').innerHTML = '&#9646;&#9646;';
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
          } else if (settings.loop) {
            currentFrame = 0;
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

    // --- Navigation helpers ---
    function findNextUnreviewed(afterIdx) {
      for (let i = afterIdx + 1; i < SCENARIOS.length; i++) {
        if (isUnreviewed(SCENARIOS[i].name)) return i;
      }
      // Wrap around
      for (let i = 0; i <= afterIdx; i++) {
        if (isUnreviewed(SCENARIOS[i].name)) return i;
      }
      return -1;
    }
    function jumpNextUnreviewed() {
      const next = findNextUnreviewed(currentIdx);
      if (next >= 0) loadScenario(next);
    }

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
      // Auto-advance: skip already-reviewed scenarios
      if (status === 'pass' || status === 'fail') {
        const next = findNextUnreviewed(currentIdx);
        if (next >= 0 && next !== currentIdx) loadScenario(next);
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
      if (e.target.tagName === 'TEXTAREA') {
        if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); e.target.blur(); }
        return;
      }
      if (e.target.tagName === 'INPUT') return;
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
        case 'l': case 'L': toggleSetting('loop'); break;
        case '1': setReview('pass'); break;
        case '2': setReview('fail'); break;
        case '3': setReview('unreviewed'); break;
        case 'Enter': e.preventDefault(); document.getElementById('notes-input').focus(); break;
        case 'Tab': e.preventDefault(); jumpNextUnreviewed(); break;
        case 'n': case 'N': loadScenario((currentIdx + 1) % SCENARIOS.length); break;
        case 'p': case 'P': loadScenario((currentIdx - 1 + SCENARIOS.length) % SCENARIOS.length); break;
      }
    });

    // --- Init ---
    updateSpeed();
    updateSettingsUI();
    buildSidebar();
    loadScenario(0);
    """;

    private static string HtmlFooter() => "";
}
