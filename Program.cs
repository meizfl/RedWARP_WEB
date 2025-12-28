// Program.cs - ASP.NET Core Minimal API
// Target: .NET 8
// Build: dotnet new web -n RedWarpWeb && cd RedWarpWeb
//        Replace Program.cs with this content
//        dotnet run

using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.IO.Compression;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Папка для тимчасових файлів кожного користувача
var workDir = Path.Combine(Directory.GetCurrentDirectory(), "work");
Directory.CreateDirectory(workDir);

// Папка для бінарників
var binDir = Path.Combine(Directory.GetCurrentDirectory(), "bin");
Directory.CreateDirectory(binDir);

app.UseStaticFiles();

// Головна сторінка
app.MapGet("/", () => Results.Content(GetHtmlPage(), "text/html"));

// API endpoint для генерації конфігу
app.MapPost("/api/generate", async (GenerateRequest req) =>
{
    // Створюємо унікальну папку для цього запиту
    string sessionId = Guid.NewGuid().ToString("N");
    string sessionDir = Path.Combine(workDir, sessionId);
    Directory.CreateDirectory(sessionDir);

    try
    {
        // Перевіряємо наявність wgcf або завантажуємо його
        string? wgcfPath = await EnsureWgcfExists();
        
        if (wgcfPath == null)
            return Results.Json(new { 
                success = false, 
                message = "Не вдалося знайти або завантажити wgcf. Перевірте інтернет-з'єднання." 
            });

        // Виконуємо wgcf register
        if (!RunCommand(wgcfPath, sessionDir, "register", "--accept-tos"))
            return Results.Json(new { success = false, message = "Command execution error: wgcf register" });

        // Виконуємо wgcf generate
        if (!RunCommand(wgcfPath, sessionDir, "generate"))
            return Results.Json(new { success = false, message = "Command execution error: wgcf generate" });

        string profilePath = Path.Combine(sessionDir, "wgcf-profile.conf");
        if (!File.Exists(profilePath))
            return Results.Json(new { success = false, message = "wgcf-profile.conf not found after generate" });

        // Обробляємо конфіг
        string outputPath = Path.Combine(sessionDir, "RedWARP.conf");
        await ProcessConfigFile(profilePath, outputPath, req);

        if (!File.Exists(outputPath))
            return Results.Json(new { success = false, message = "RedWARP.conf was not created" });

        // Читаємо готовий конфіг
        string configContent = await File.ReadAllTextAsync(outputPath);
        
        // Видаляємо тимчасові файли
        try { Directory.Delete(sessionDir, true); } catch { }

        return Results.Json(new { 
            success = true, 
            message = "Конфіг успішно згенеровано!",
            config = configContent,
            filename = "RedWARP.conf"
        });
    }
    catch (Exception ex)
    {
        // Прибираємо тимчасову папку у разі помилки
        try { Directory.Delete(sessionDir, true); } catch { }
        return Results.Json(new { success = false, message = "Error: " + ex.Message });
    }
});

app.Run();

// ===== Допоміжні методи =====

static async Task<string?> EnsureWgcfExists()
{
    string binDir = Path.Combine(Directory.GetCurrentDirectory(), "bin");
    
    // Шукаємо існуючі файли wgcf
    string[] wgcfCandidates = Directory.GetFiles(binDir, "wgcf*");
    
    if (wgcfCandidates.Length > 0)
    {
        Array.Sort(wgcfCandidates);
        string existingPath = wgcfCandidates[0];
        MakeExecutable(existingPath);
        return existingPath;
    }

    // Якщо не знайдено - завантажуємо
    Console.WriteLine("wgcf не знайдено, завантажуємо останню версію...");
    return await DownloadLatestWgcf(binDir);
}

static async Task<string?> DownloadLatestWgcf(string binDir)
{
    try
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "RedWARP-Generator");
        
        // Отримуємо інформацію про останній реліз
        const string apiUrl = "https://api.github.com/repos/ViRb3/wgcf/releases/latest";
        var response = await httpClient.GetStringAsync(apiUrl);
        var releaseInfo = JsonDocument.Parse(response);
        
        var assets = releaseInfo.RootElement.GetProperty("assets");
        
        // Визначаємо архітектуру системи
        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "armv7",
            Architecture.X86 => "386",
            _ => "amd64"
        };
        
        string os = "";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            os = "linux";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            os = "windows";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            os = "darwin";
        else
            os = "linux"; // fallback
        
        // Шукаємо відповідний файл
        string? downloadUrl = null;
        string fileName = $"wgcf_{os}_{arch}";
        if (os == "windows") fileName += ".exe";
        
        foreach (var asset in assets.EnumerateArray())
        {
            string assetName = asset.GetProperty("name").GetString() ?? "";
            if (assetName.Contains(os) && assetName.Contains(arch))
            {
                downloadUrl = asset.GetProperty("browser_download_url").GetString();
                break;
            }
        }
        
        if (downloadUrl == null)
        {
            Console.WriteLine($"Не знайдено підходящого файлу для {os}_{arch}");
            return null;
        }
        
        Console.WriteLine($"Завантажуємо: {downloadUrl}");
        
        // Завантажуємо файл
        var fileBytes = await httpClient.GetByteArrayAsync(downloadUrl);
        string targetPath = Path.Combine(binDir, fileName);
        
        await File.WriteAllBytesAsync(targetPath, fileBytes);
        MakeExecutable(targetPath);
        
        Console.WriteLine($"✓ wgcf успішно завантажено: {targetPath}");
        return targetPath;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Помилка завантаження wgcf: {ex.Message}");
        return null;
    }
}

static void MakeExecutable(string filePath)
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
        try
        {
            var chmodPsi = new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{filePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(chmodPsi);
            p?.WaitForExit();
        }
        catch { /* ігноруємо, якщо не вдалося */ }
    }
}

static async Task ProcessConfigFile(string inputPath, string outputPath, GenerateRequest req)
{
    using var infile = new StreamReader(inputPath);
    using var outfile = new StreamWriter(outputPath, false, new UTF8Encoding(false));

    string? line;
    bool inInterface = false;
    var rng = new Random();

    while ((line = await infile.ReadLineAsync()) != null)
    {
        if (line.StartsWith("[Interface]")) inInterface = true;
        else if (line.StartsWith("[")) inInterface = false;

        if (!req.Ipv6Enabled)
        {
            line = RemoveAll(line, ", 2606:4700");
            line = RemoveAll(line, ", ::/0");
        }

        if (inInterface && line.StartsWith("PrivateKey =") && req.AmneziaEnabled)
        {
            await outfile.WriteLineAsync(line);
            int Jc = req.Randomize ? rng.Next(1, 129) : 120;
            int Jmin = req.Randomize ? rng.Next(1, 401) : 23;
            int Jmax = req.Randomize ? rng.Next(Jmin + 1, 1281) : 911;
            await outfile.WriteLineAsync("S1 = 0");
            await outfile.WriteLineAsync("S2 = 0");
            await outfile.WriteLineAsync($"Jc = {Jc}");
            await outfile.WriteLineAsync($"Jmin = {Jmin}");
            await outfile.WriteLineAsync($"Jmax = {Jmax}");
            if (req.Randomize)
            {
                await outfile.WriteLineAsync($"H1 = {rng.Next(1, 5)}");
                await outfile.WriteLineAsync($"H2 = {rng.Next(1, 5)}");
                await outfile.WriteLineAsync($"H3 = {rng.Next(1, 5)}");
                await outfile.WriteLineAsync($"H4 = {rng.Next(1, 5)}");
            }
            else
            {
                await outfile.WriteLineAsync("H1 = 1");
                await outfile.WriteLineAsync("H2 = 2");
                await outfile.WriteLineAsync("H3 = 3");
                await outfile.WriteLineAsync("H4 = 4");
            }
            continue;
        }
        else if (line.StartsWith("MTU = "))
        {
            await outfile.WriteLineAsync($"MTU = {req.Mtu}");
        }
        else if (line.StartsWith("Endpoint = "))
        {
            await outfile.WriteLineAsync($"Endpoint = {req.Endpoint}");
        }
        else if (line.StartsWith("DNS = "))
        {
            var sb = new StringBuilder();
            sb.Append("DNS = ");
            sb.Append(req.DnsV4);
            if (req.Ipv6Enabled && !string.IsNullOrWhiteSpace(req.DnsV6))
            {
                sb.Append(", ");
                sb.Append(req.DnsV6);
            }
            await outfile.WriteLineAsync(sb.ToString());
        }
        else
        {
            await outfile.WriteLineAsync(line);
        }
    }
}

static string RemoveAll(string input, string token)
{
    while (true)
    {
        int idx = input.IndexOf(token, StringComparison.Ordinal);
        if (idx < 0) break;
        input = input.Remove(idx, token.Length);
    }
    return input;
}

static bool RunCommand(string fileName, string workingDir, params string[] args)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}

static string GetHtmlPage() => """
<!DOCTYPE html>
<html lang="uk">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>The MeizFL's RedWARP Generator</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            background: linear-gradient(135deg, #111827 0%, #020617 50%, #0b1120 100%);
            color: #e5e7eb;
            min-height: 100vh;
            display: flex;
            flex-direction: column;
        }

        .top-bar {
            background: #020617;
            border-bottom: 1px solid #111827;
            padding: 12px 24px;
        }

        .top-bar-content {
            max-width: 1200px;
            margin: 0 auto;
        }

        .title {
            font-size: 18px;
            font-weight: 600;
            color: white;
        }

        .container {
            flex: 1;
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 24px;
        }

        .main-content {
            width: 100%;
            max-width: 1100px;
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 24px;
        }

        .card {
            background: #050816;
            border: 1px solid #1f2937;
            border-radius: 22px;
            padding: 24px;
            box-shadow: 0 18px 30px rgba(0, 0, 0, 0.6);
        }

        .card-header {
            font-size: 20px;
            font-weight: 600;
            margin-bottom: 8px;
        }

        .card-description {
            font-size: 12px;
            color: #9ca3af;
            margin-bottom: 20px;
        }

        .form-group {
            margin-bottom: 14px;
        }

        .form-row {
            display: grid;
            grid-template-columns: 140px 1fr;
            gap: 12px;
            align-items: center;
        }

        label {
            color: #e5e7eb;
            font-size: 13px;
        }

        input[type="text"], select {
            background: #0b1120;
            border: 1px solid #1f2937;
            border-radius: 8px;
            padding: 8px 12px;
            color: #e5e7eb;
            font-size: 13px;
            width: 100%;
        }

        input[type="text"]:focus, select:focus {
            outline: none;
            border-color: #ef4444;
        }

        input[type="text"]::placeholder {
            color: #6b7280;
        }

        select {
            cursor: pointer;
        }

        .dns-row {
            display: grid;
            grid-template-columns: 130px 1fr;
            gap: 8px;
        }

        .divider {
            height: 1px;
            background: #1f2937;
            margin: 16px 0;
        }

        .bottom-section {
            display: grid;
            grid-template-columns: 1fr auto;
            gap: 12px;
            align-items: center;
        }

        .status {
            color: #e5e7eb;
            font-size: 13px;
            word-wrap: break-word;
        }

        .status.error {
            color: #ef4444;
        }

        .status.success {
            color: #10b981;
        }

        .btn {
            background: #ef4444;
            color: white;
            border: 1px solid #f97373;
            border-radius: 8px;
            padding: 10px 24px;
            font-size: 14px;
            font-weight: 500;
            cursor: pointer;
            transition: background 0.2s;
            min-width: 150px;
        }

        .btn:hover:not(:disabled) {
            background: #dc2626;
        }

        .btn:disabled {
            opacity: 0.5;
            cursor: not-allowed;
        }

        .btn-download {
            background: #10b981;
            border-color: #34d399;
            margin-top: 12px;
        }

        .btn-download:hover:not(:disabled) {
            background: #059669;
        }

        .footer {
            text-align: center;
            padding: 16px;
            color: #6b7280;
            font-size: 11px;
        }

        .config-output {
            margin-top: 16px;
            background: #0b1120;
            border: 1px solid #1f2937;
            border-radius: 8px;
            padding: 12px;
            max-height: 300px;
            overflow-y: auto;
            font-family: 'Courier New', monospace;
            font-size: 12px;
            white-space: pre-wrap;
            word-wrap: break-word;
            display: none;
        }

        .config-output.show {
            display: block;
        }

        .download-section {
            display: none;
            margin-top: 12px;
            text-align: center;
        }

        .download-section.show {
            display: block;
        }

        @media (max-width: 900px) {
            .main-content {
                grid-template-columns: 1fr;
            }
        }
    </style>
</head>
<body>
    <div class="top-bar">
        <div class="top-bar-content">
            <div class="title">The MeizFL's RedWARP Generator</div>
        </div>
    </div>

    <div class="container">
        <div class="main-content">
            <!-- Ліва картка -->
            <div class="card">
                <div class="card-header">Параметри підключення</div>
                <div class="card-description">Endpoint, MTU і AmneziaWG-параметри для RedWARP.</div>
                
                <div class="form-group">
                    <div class="form-row">
                        <label for="endpoint">Endpoint:</label>
                        <input type="text" id="endpoint" value="162.159.192.1:4500">
                    </div>
                </div>

                <div class="form-group">
                    <div class="form-row">
                        <label for="mtu">MTU:</label>
                        <input type="text" id="mtu" value="1420" style="width: 120px;">
                    </div>
                </div>

                <div class="form-group">
                    <div class="form-row">
                        <label for="amnezia">AmneziaWG:</label>
                        <select id="amnezia" style="width: 120px;">
                            <option value="true">Yes</option>
                            <option value="false">No</option>
                        </select>
                    </div>
                </div>

                <div class="form-group">
                    <div class="form-row">
                        <label for="randomize">Randomize:</label>
                        <select id="randomize" style="width: 120px;">
                            <option value="true">Yes</option>
                            <option value="false" selected>No</option>
                        </select>
                    </div>
                </div>
            </div>

            <!-- Права картка -->
            <div class="card">
                <div class="card-header">IPv6, DNS та генерація</div>
                <div class="card-description">Увімкни IPv6, обери DNS для IPv4/IPv6 — і натисни «Сгенерировать».</div>
                
                <div class="form-group">
                    <div class="form-row">
                        <label for="ipv6">IPv6:</label>
                        <select id="ipv6" style="width: 120px;" onchange="updateIpv6UI()">
                            <option value="true">Yes</option>
                            <option value="false">No</option>
                        </select>
                    </div>
                </div>

                <div class="form-group">
                    <div class="form-row">
                        <label for="dnsv4">DNS IPv4:</label>
                        <div class="dns-row">
                            <select id="dnsv4" onchange="updateDnsCustom()">
                                <option value="0">OpenDNS</option>
                                <option value="1">Cloudflare</option>
                                <option value="2">Google</option>
                                <option value="3">Quad9</option>
                                <option value="4">Custom</option>
                            </select>
                            <input type="text" id="dnsv4custom" placeholder="1.1.1.1, 8.8.8.8" disabled>
                        </div>
                    </div>
                </div>

                <div class="form-group">
                    <div class="form-row">
                        <label for="dnsv6">DNS IPv6:</label>
                        <div class="dns-row">
                            <select id="dnsv6" onchange="updateDnsCustom()">
                                <option value="0">OpenDNS</option>
                                <option value="1">Cloudflare</option>
                                <option value="2">Google</option>
                                <option value="3">Quad9</option>
                                <option value="4">Custom</option>
                            </select>
                            <input type="text" id="dnsv6custom" placeholder="2606:4700:4700::1111" disabled>
                        </div>
                    </div>
                </div>

                <div class="divider"></div>

                <div class="bottom-section">
                    <div id="status" class="status">Готово до генерації.</div>
                    <button id="generateBtn" class="btn" onclick="generate()">Сгенерировать</button>
                </div>

                <div id="downloadSection" class="download-section">
                    <button id="downloadBtn" class="btn btn-download" onclick="downloadConfig()">📥 Завантажити RedWARP.conf</button>
                </div>

                <div id="configOutput" class="config-output"></div>
            </div>
        </div>
    </div>

    <div class="footer">
        © 2025 MeizFL • RedWARP UI (ASP.NET Core) • Auto-download wgcf
    </div>

    <script>
        let generatedConfig = null;

        function updateIpv6UI() {
            const ipv6 = document.getElementById('ipv6').value === 'true';
            document.getElementById('dnsv6').disabled = !ipv6;
            document.getElementById('dnsv6custom').disabled = !ipv6 || document.getElementById('dnsv6').value !== '4';
        }

        function updateDnsCustom() {
            document.getElementById('dnsv4custom').disabled = document.getElementById('dnsv4').value !== '4';
            const ipv6 = document.getElementById('ipv6').value === 'true';
            document.getElementById('dnsv6custom').disabled = !ipv6 || document.getElementById('dnsv6').value !== '4';
        }

        function getDnsV4() {
            const sel = document.getElementById('dnsv4').value;
            const custom = document.getElementById('dnsv4custom').value;
            const presets = [
                '208.67.222.222, 208.67.220.220',
                '1.1.1.1, 1.0.0.1',
                '8.8.8.8, 8.8.4.4',
                '9.9.9.9, 149.112.112.112'
            ];
            return sel === '4' ? custom : presets[parseInt(sel)];
        }

        function getDnsV6() {
            if (document.getElementById('ipv6').value !== 'true') return '';
            const sel = document.getElementById('dnsv6').value;
            const custom = document.getElementById('dnsv6custom').value;
            const presets = [
                '2620:119:35::35, 2620:119:53::53',
                '2606:4700:4700::1111, 2606:4700:4700::1001',
                '2001:4860:4860::8888, 2001:4860:4860::8844',
                '2620:fe::fe, 2620:fe::9'
            ];
            return sel === '4' ? custom : presets[parseInt(sel)];
        }

        async function generate() {
            const statusEl = document.getElementById('status');
            const btn = document.getElementById('generateBtn');
            const outputEl = document.getElementById('configOutput');
            const downloadSection = document.getElementById('downloadSection');
            
            statusEl.textContent = '⏳ Запуск wgcf на сервері, зачекай...';
            statusEl.className = 'status';
            btn.disabled = true;
            outputEl.classList.remove('show');
            downloadSection.classList.remove('show');
            generatedConfig = null;

            try {
                const response = await fetch('/api/generate', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        endpoint: document.getElementById('endpoint').value,
                        mtu: document.getElementById('mtu').value,
                        ipv6Enabled: document.getElementById('ipv6').value === 'true',
                        amneziaEnabled: document.getElementById('amnezia').value === 'true',
                        randomize: document.getElementById('randomize').value === 'true',
                        dnsV4: getDnsV4(),
                        dnsV6: getDnsV6()
                    })
                });

                const result = await response.json();
                
                if (result.success) {
                    statusEl.textContent = '✅ ' + result.message;
                    statusEl.className = 'status success';
                    generatedConfig = result.config;
                    outputEl.textContent = result.config;
                    outputEl.classList.add('show');
                    downloadSection.classList.add('show');
                } else {
                    statusEl.textContent = '❌ ' + result.message;
                    statusEl.className = 'status error';
                }
            } catch (error) {
                statusEl.textContent = '❌ Помилка: ' + error.message;
                statusEl.className = 'status error';
            } finally {
                btn.disabled = false;
            }
        }

        function downloadConfig() {
            if (!generatedConfig) return;
            
            const blob = new Blob([generatedConfig], { type: 'text/plain' });
            const url = window.URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = 'RedWARP.conf';
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            window.URL.revokeObjectURL(url);
        }

        updateIpv6UI();
    </script>
</body>
</html>
""";

// ===== DTO =====
record GenerateRequest(
    string Endpoint,
    string Mtu,
    bool Ipv6Enabled,
    bool AmneziaEnabled,
    bool Randomize,
    string DnsV4,
    string DnsV6
);
