// Program.fs - ASP.NET Core Minimal API на F#
// Target: .NET 8
// Build: dotnet new web -lang F# -n RedWarpWeb && cd RedWarpWeb
//        Заменить Program.fs этим содержимым
//        dotnet run

open System
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http

// ===== DTO =====
type GenerateRequest = {
    Endpoint: string
    Mtu: string
    Ipv6Enabled: bool
    AmneziaEnabled: bool
    DnsV4: string
    DnsV6: string
    I1: string
    I2: string
    I3: string
    I4: string
    I5: string
}

// ===== Helper Functions =====
let makeExecutable (filePath: string) =
    if RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then
        try
            let psi = ProcessStartInfo(
                FileName = "chmod",
                Arguments = $"+x \"{filePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            )
            use p = Process.Start(psi)
            p.WaitForExit()
        with _ -> ()

let runCommand (fileName: string) (workingDir: string) (args: string list) =
    try
        let psi = ProcessStartInfo(
            FileName = fileName,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        )
        args |> List.iter psi.ArgumentList.Add
        use p = Process.Start(psi)
        p.WaitForExit()
        p.ExitCode = 0
    with _ -> false

let removeAll (input: string) (token: string) =
    let rec loop (s: string) =
        match s.IndexOf(token, StringComparison.Ordinal) with
        | idx when idx < 0 -> s
        | idx -> loop (s.Remove(idx, token.Length))
    loop input

let rec downloadLatestWgcf (binDir: string) = async {
    try
        use httpClient = new System.Net.Http.HttpClient()
        httpClient.DefaultRequestHeaders.Add("User-Agent", "RedWARP-Generator")
        
        let apiUrl = "https://api.github.com/repos/ViRb3/wgcf/releases/latest"
        let! response = httpClient.GetStringAsync(apiUrl) |> Async.AwaitTask
        let releaseInfo = JsonDocument.Parse(response)
        
        let assets = releaseInfo.RootElement.GetProperty("assets")
        
        let arch = 
            match RuntimeInformation.ProcessArchitecture with
            | Architecture.X64 -> "amd64"
            | Architecture.Arm64 -> "arm64"
            | Architecture.Arm -> "armv7"
            | Architecture.X86 -> "386"
            | _ -> "amd64"
        
        let os = 
            if RuntimeInformation.IsOSPlatform(OSPlatform.Linux) then "linux"
            elif RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then "windows"
            elif RuntimeInformation.IsOSPlatform(OSPlatform.OSX) then "darwin"
            else "linux"
        
        let fileName = 
            let base' = $"wgcf_{os}_{arch}"
            if os = "windows" then base' + ".exe" else base'
        
        let downloadUrl =
            assets.EnumerateArray()
            |> Seq.tryFind (fun asset ->
                let name = asset.GetProperty("name").GetString()
                name.Contains(os) && name.Contains(arch))
            |> Option.bind (fun asset -> 
                asset.GetProperty("browser_download_url").GetString() |> Some)
        
        match downloadUrl with
        | None ->
            printfn $"No suitable file found for {os}_{arch}"
            return None
        | Some url ->
            printfn $"Downloading: {url}"
            let! fileBytes = httpClient.GetByteArrayAsync(url) |> Async.AwaitTask
            let targetPath = Path.Combine(binDir, fileName)
            
            do! File.WriteAllBytesAsync(targetPath, fileBytes) |> Async.AwaitTask
            makeExecutable targetPath
            
            printfn $"✓ wgcf successfully downloaded: {targetPath}"
            return Some targetPath
    with ex ->
        printfn $"Error downloading wgcf: {ex.Message}"
        return None
}

let ensureWgcfExists () = async {
    let binDir = Path.Combine(Directory.GetCurrentDirectory(), "bin")
    
    let wgcfCandidates = Directory.GetFiles(binDir, "wgcf*")
    
    if wgcfCandidates.Length > 0 then
        Array.Sort(wgcfCandidates)
        let existingPath = wgcfCandidates.[0]
        makeExecutable existingPath
        return Some existingPath
    else
        printfn "wgcf not found, downloading latest version..."
        return! downloadLatestWgcf binDir
}

let processConfigFile (inputPath: string) (outputPath: string) (req: GenerateRequest) = async {
    use infile = new StreamReader(inputPath)
    use outfile = new StreamWriter(outputPath, false, UTF8Encoding(false))
    
    let mutable inInterface = false
    let mutable line = infile.ReadLine()
    
    while line <> null do
        let mutable currentLine = line
        
        if currentLine.StartsWith("[Interface]") then
            inInterface <- true
        elif currentLine.StartsWith("[") then
            inInterface <- false
        
        // Handle Address when IPv6 is disabled
        if currentLine.StartsWith("Address = ") && not req.Ipv6Enabled then
            match currentLine.IndexOf(',') with
            | commaIndex when commaIndex > 0 ->
                currentLine <- currentLine.Substring(0, commaIndex).TrimEnd()
            | _ -> ()
        
        // Remove ::/0 from AllowedIPs
        if currentLine.StartsWith("AllowedIPs = ") && not req.Ipv6Enabled then
            currentLine <- removeAll currentLine ", ::/0"
            currentLine <- removeAll currentLine ",::/0"
        
        if inInterface && currentLine.StartsWith("PrivateKey =") && req.AmneziaEnabled then
            do! outfile.WriteLineAsync(currentLine) |> Async.AwaitTask
            let rng = Random()
            let h3 = rng.Next(2073986817, 2147128181)
            do! outfile.WriteLineAsync("S1 = 0") |> Async.AwaitTask
            do! outfile.WriteLineAsync("S2 = 0") |> Async.AwaitTask
            do! outfile.WriteLineAsync("Jc = 4") |> Async.AwaitTask
            do! outfile.WriteLineAsync("Jmin = 40") |> Async.AwaitTask
            do! outfile.WriteLineAsync("Jmax = 70") |> Async.AwaitTask
            do! outfile.WriteLineAsync("H1 = 1") |> Async.AwaitTask
            do! outfile.WriteLineAsync("H2 = 2") |> Async.AwaitTask
            do! outfile.WriteLineAsync($"H3 = {h3}") |> Async.AwaitTask
            do! outfile.WriteLineAsync($"I1 = {req.I1}") |> Async.AwaitTask
            do! outfile.WriteLineAsync($"I2 = {req.I2}") |> Async.AwaitTask
            do! outfile.WriteLineAsync($"I3 = {req.I3}") |> Async.AwaitTask
            do! outfile.WriteLineAsync($"I4 = {req.I4}") |> Async.AwaitTask
            do! outfile.WriteLineAsync($"I5 = {req.I5}") |> Async.AwaitTask
        elif currentLine.StartsWith("MTU = ") then
            do! outfile.WriteLineAsync($"MTU = {req.Mtu}") |> Async.AwaitTask
        elif currentLine.StartsWith("Endpoint = ") then
            do! outfile.WriteLineAsync($"Endpoint = {req.Endpoint}") |> Async.AwaitTask
        elif currentLine.StartsWith("DNS = ") then
            let sb = StringBuilder()
            sb.Append("DNS = ") |> ignore
            sb.Append(req.DnsV4) |> ignore
            if req.Ipv6Enabled && not (String.IsNullOrWhiteSpace(req.DnsV6)) then
                sb.Append(", ") |> ignore
                sb.Append(req.DnsV6) |> ignore
            do! outfile.WriteLineAsync(sb.ToString()) |> Async.AwaitTask
        else
            do! outfile.WriteLineAsync(currentLine) |> Async.AwaitTask
        
        line <- infile.ReadLine()
}

// ===== Junk Packet Generator =====
// Generates 5 UDP junk packets that mimic real protocols to evade DPI.
// Packet types (fixed order, randomised fields):
//   I1 = SIP REGISTER  I2 = TLS ClientHello  I3 = TLS ServerHello
//   I4 = TLS AppData   I5 = HTTP GET

let private toHex (bytes: byte[]) =
    let sb = StringBuilder(bytes.Length * 2)
    for b in bytes do sb.Append(b.ToString("x2")) |> ignore
    sb.ToString()

let private randBytes (rng: Random) n =
    let b = Array.zeroCreate<byte> n
    rng.NextBytes(b)
    b

let private randHex (rng: Random) n = toHex (randBytes rng n)

let private randIp (rng: Random) =
    sprintf "%d.%d.%d.%d" (rng.Next(10,240)) (rng.Next(1,255)) (rng.Next(1,255)) (rng.Next(1,255))

let private randPort (rng: Random) = rng.Next(1024, 65535)

// ---- I1: SIP REGISTER ----
let private makeSipRegister (rng: Random) =
    let ip       = randIp rng
    let srcPort  = randPort rng
    let branch   = randHex rng 26
    let callId   = randHex rng 16
    let fromTag  = randHex rng 8
    let domains  = [| "google.com"; "youtube.com"; "cloudflare.com"; "apple.com"; "microsoft.com"; "facebook.com"; "instagram.com"; "whatsapp.com"; "wikipedia.org"; "amazon.com"; "bing.com"; "reddit.com"; "chatgpt.com"; "netflix.com"; "tiktok.com"; "akamai.com"; "fastly.com"; "yandex.ru"; "vk.com"; "mail.ru"; "dzen.ru"; "ozon.ru"; "wildberries.ru"; "avito.ru"; "gosuslugi.ru"; "sber.ru"; "vkontakte.ru"; "ok.ru"; "rambler.ru"; "ria.ru"; "baidu.com"; "qq.com"; "taobao.com"; "weibo.com"; "163.com"; "alibaba.com"; "tmall.com"; "jd.com"; "douyin.com"; "sina.com.cn"; "tencent.com"; "pinduoduo.com"; "ximalaya.com"; "yahoo.com"; "linkedin.com"; "twitch.tv"; "spotify.com"; "adobe.com"; "ebay.com"; "paypal.com"; "booking.com"; "airbnb.com"; "aliexpress.com"; "huawei.com"; "samsung.com"; "sony.com"; "nvidia.com"; "intel.com"; "oracle.com"; "ibm.com"; "zoom.us"; "discord.com"; "telegram.org"; "github.com"; "stackoverflow.com"; "medium.com"; "quora.com"; "bbc.com"; "cnn.com"; "nytimes.com"; "washingtonpost.com"; "naver.com"; "daum.net"; "line.me" |]
    let domain   = domains.[rng.Next(domains.Length)]
    let expires  = rng.Next(3600, 7200)
    let cseq     = rng.Next(1, 10)
    let body =
        sprintf "REGISTER sip:%s SIP/2.0\r\nVia: SIP/2.0/UDP %s:%d;branch=z9hG4bK%s\r\nMax-Forwards: 70\r\nTo: <sip:user@%s>\r\nFrom: <sip:user@%s>;tag=%s\r\nCall-ID: %s\r\nCSeq: %d REGISTER\r\nContact: <sip:user@%s:%d>\r\nUser-Agent: Bria 5.0.0\r\nExpires: %d\r\nContent-Length: 0\r\n\r\n"
            domain ip srcPort branch domain domain fromTag callId cseq ip srcPort expires
    let bytes = Encoding.ASCII.GetBytes(body)
    sprintf "<b 0x%s>" (toHex bytes)

// ---- I2: TLS ClientHello ----
let private makeTlsClientHello (rng: Random) =
    let hosts = [| "google.com"; "youtube.com"; "cloudflare.com"; "apple.com"; "microsoft.com"; "facebook.com"; "instagram.com"; "whatsapp.com"; "wikipedia.org"; "amazon.com"; "bing.com"; "reddit.com"; "chatgpt.com"; "netflix.com"; "tiktok.com"; "akamai.com"; "fastly.com"; "yandex.ru"; "vk.com"; "mail.ru"; "dzen.ru"; "ozon.ru"; "wildberries.ru"; "avito.ru"; "gosuslugi.ru"; "sber.ru"; "vkontakte.ru"; "ok.ru"; "rambler.ru"; "ria.ru"; "baidu.com"; "qq.com"; "taobao.com"; "weibo.com"; "163.com"; "alibaba.com"; "tmall.com"; "jd.com"; "douyin.com"; "sina.com.cn"; "tencent.com"; "pinduoduo.com"; "ximalaya.com"; "yahoo.com"; "linkedin.com"; "twitch.tv"; "spotify.com"; "adobe.com"; "ebay.com"; "paypal.com"; "booking.com"; "airbnb.com"; "aliexpress.com"; "huawei.com"; "samsung.com"; "sony.com"; "nvidia.com"; "intel.com"; "oracle.com"; "ibm.com"; "zoom.us"; "discord.com"; "telegram.org"; "github.com"; "stackoverflow.com"; "medium.com"; "quora.com"; "bbc.com"; "cnn.com"; "nytimes.com"; "washingtonpost.com"; "naver.com"; "daum.net"; "line.me" |]
    let sni   = hosts.[rng.Next(hosts.Length)]
    let sniBytes = Encoding.ASCII.GetBytes(sni)
    let sniLen   = sniBytes.Length
    // Random 32-byte client random
    let clientRandom = randBytes rng 32
    // Session ID (0 length)
    // Cipher suites: pick 2-4 from a realistic set
    let allCiphers = [| 0xC02Bus; 0xC02Cus; 0xCCA8us; 0xCCA9us; 0xC013us; 0xC014us; 0x009Cus; 0x009Dus |]
    let numCiphers = rng.Next(2, 5)
    let ciphers = Array.init numCiphers (fun _ -> allCiphers.[rng.Next(allCiphers.Length)])
    // Build extensions: SNI + supported_groups + ec_point_formats
    let sniExt =
        [| 0x00uy; 0x00uy // extension type: server_name
           0x00uy; byte (sniLen + 5)  // ext len
           0x00uy; byte (sniLen + 3)  // list len
           0x00uy                     // name type: host_name
           0x00uy; byte sniLen |]
        |> Array.append (Array.map byte sniBytes)
    let sgExt =
        [| 0x00uy; 0x0Auy  // supported_groups
           0x00uy; 0x0Auy
           0x00uy; 0x08uy
           0x7Buy; 0x88uy; 0x65uy; 0x2Cuy; 0xE4uy; 0x6Buy; 0x47uy; 0xABuy |]
    let epExt =
        [| 0x00uy; 0x0Buy  // ec_point_formats
           0x00uy; 0x04uy
           0x03uy; 0x00uy; 0x01uy; 0x02uy |]
    let exts = Array.concat [sniExt; sgExt; epExt]
    let extLen = exts.Length
    // Ciphers bytes
    let cipherBytes =
        ciphers |> Array.collect (fun c -> [| byte (c >>> 8); byte c |])
    let cipherSuitesLen = cipherBytes.Length
    // Hello body
    let helloBody =
        Array.concat [
            clientRandom                     // 32 bytes random
            [| 0x00uy |]                     // session id length
            [| 0x00uy; byte cipherSuitesLen |]
            cipherBytes
            [| 0x01uy; 0x00uy |]             // compression methods
            [| 0x00uy; byte extLen |]
            exts
        ]
    let helloLen = helloBody.Length
    // Handshake header
    let handshake =
        Array.concat [
            [| 0x01uy                          // ClientHello
               0x00uy; 0x00uy; byte helloLen   // length (3 bytes, simplified)
               0x03uy; 0x03uy |]               // TLS 1.2
            helloBody
        ]
    let hsLen = handshake.Length
    // TLS record
    let record =
        Array.concat [
            [| 0x16uy; 0x03uy; 0x03uy
               byte (hsLen >>> 8); byte hsLen |]
            handshake
        ]
    sprintf "<b 0x%s>" (toHex record)

// ---- I3: TLS ServerHello ----
let private makeTlsServerHello (rng: Random) =
    let serverRandom = randBytes rng 32
    let ciphers = [| 0xC02Fus; 0xC030us; 0xCCA8us; 0x009Cus; 0x009Dus; 0xC013us; 0xC014us |]
    let cipher  = ciphers.[rng.Next(ciphers.Length)]
    let helloBody =
        Array.concat [
            [| 0x03uy; 0x03uy |]   // TLS 1.2
            serverRandom
            [| 0x00uy |]           // session id length
            [| byte (cipher >>> 8); byte cipher |]
            [| 0x00uy |]           // compression: null
            [| 0x00uy; 0x00uy |]   // no extensions
        ]
    let helloLen = helloBody.Length
    let handshake =
        Array.concat [
            [| 0x02uy; 0x00uy; 0x00uy; byte helloLen; 0x03uy; 0x03uy |]
            serverRandom
            [| 0x00uy; byte (cipher >>> 8); byte cipher; 0x00uy |]
        ]
    let hsLen = handshake.Length
    let record =
        Array.concat [
            [| 0x16uy; 0x03uy; 0x03uy
               byte (hsLen >>> 8); byte hsLen |]
            handshake
        ]
    sprintf "<b 0x%s>" (toHex record)

// ---- I4: TLS AppData (DHE key + ChangeCipherSpec + Finished) ----
let private makeTlsAppData (rng: Random) =
    // DHE pre-master key exchange (128 random bytes)
    let dhPart = randBytes rng 128
    let keyExchangeBody =
        Array.concat [
            [| 0x10uy; 0x00uy; 0x00uy; 0x80uy |]  // Handshake type + length
            dhPart
        ]
    let keyExchangeRecord =
        Array.concat [
            [| 0x16uy; 0x03uy; 0x03uy
               0x00uy; byte (keyExchangeBody.Length) |]
            keyExchangeBody
        ]
    // ChangeCipherSpec
    let ccsRecord = [| 0x14uy; 0x03uy; 0x03uy; 0x00uy; 0x01uy; 0x01uy |]
    // Finished (52 random bytes)
    let finishedData = randBytes rng 52
    let finishedRecord =
        Array.concat [
            [| 0x16uy; 0x03uy; 0x03uy
               0x00uy; byte finishedData.Length |]
            finishedData
        ]
    let full = Array.concat [keyExchangeRecord; ccsRecord; finishedRecord]
    sprintf "<b 0x%s>" (toHex full)

// ---- I5: HTTP GET ----
let private makeHttpGet (rng: Random) =
    let hosts = [| "google.com"; "youtube.com"; "cloudflare.com"; "apple.com"; "microsoft.com"; "facebook.com"; "instagram.com"; "whatsapp.com"; "wikipedia.org"; "amazon.com"; "bing.com"; "reddit.com"; "chatgpt.com"; "netflix.com"; "tiktok.com"; "akamai.com"; "fastly.com"; "yandex.ru"; "vk.com"; "mail.ru"; "dzen.ru"; "ozon.ru"; "wildberries.ru"; "avito.ru"; "gosuslugi.ru"; "sber.ru"; "vkontakte.ru"; "ok.ru"; "rambler.ru"; "ria.ru"; "baidu.com"; "qq.com"; "taobao.com"; "weibo.com"; "163.com"; "alibaba.com"; "tmall.com"; "jd.com"; "douyin.com"; "sina.com.cn"; "tencent.com"; "pinduoduo.com"; "ximalaya.com"; "yahoo.com"; "linkedin.com"; "twitch.tv"; "spotify.com"; "adobe.com"; "ebay.com"; "paypal.com"; "booking.com"; "airbnb.com"; "aliexpress.com"; "huawei.com"; "samsung.com"; "sony.com"; "nvidia.com"; "intel.com"; "oracle.com"; "ibm.com"; "zoom.us"; "discord.com"; "telegram.org"; "github.com"; "stackoverflow.com"; "medium.com"; "quora.com"; "bbc.com"; "cnn.com"; "nytimes.com"; "washingtonpost.com"; "naver.com"; "daum.net"; "line.me" |]
    let paths = [| "/mail"; "/search"; "/index.html"; "/api/v1/status"; "/favicon.ico"; "/" |]
    let uas   = [|
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36"
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/92.0.4515.107 Safari/537.36"
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:89.0) Gecko/20100101 Firefox/89.0"
        "Mozilla/5.0 (iPhone; CPU iPhone OS 14_6 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/14.1.1 Mobile/15E148 Safari/604.1"
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36"
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36"
        "Mozilla/5.0 (Windows NT 11.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36"
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36"
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36"
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36"
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:149.0) Gecko/20100101 Firefox/149.0"
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:148.0) Gecko/20100101 Firefox/148.0"
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 15.7; rv:149.0) Gecko/20100101 Firefox/149.0"
        "Mozilla/5.0 (X11; Linux x86_64; rv:149.0) Gecko/20100101 Firefox/149.0"
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 15_7_5) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.0 Safari/605.1.15"
        "Mozilla/5.0 (iPhone; CPU iPhone OS 18_7_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.0 Mobile/15E148 Safari/604.1"
        "Mozilla/5.0 (iPad; CPU OS 18_7_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.0 Mobile/15E148 Safari/604.1"
        "Mozilla/5.0 (Linux; Android 15; SM-S931B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.7680.178 Mobile Safari/537.36"
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36 Edg/146.0.0.0"
    |]
    let host = hosts.[rng.Next(hosts.Length)]
    let path = paths.[rng.Next(paths.Length)]
    let ua   = uas.[rng.Next(uas.Length)]
    let body =
        sprintf "GET %s HTTP/1.1\r\nHost: %s\r\nUser-Agent: %s\r\nAccept: text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8\r\nAccept-Language: en-US,en;q=0.5\r\nAccept-Encoding: gzip, deflate, br\r\nConnection: keep-alive\r\n\r\n"
            path host ua
    let bytes = Encoding.ASCII.GetBytes(body)
    sprintf "<b 0x%s>" (toHex bytes)

let generateJunkPackets () =
    let rng = Random()
    {|
        I1 = makeSipRegister rng
        I2 = makeTlsClientHello rng
        I3 = makeTlsServerHello rng
        I4 = makeTlsAppData rng
        I5 = makeHttpGet rng
    |}

let getHtmlPage () = """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>MeizFL's RedWARP Generator</title>
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
            grid-template-columns: 1.4fr 1fr;
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
            max-width: 420px;
            overflow-y: auto;
            overflow-x: auto;
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

        .junk-header {
            display: flex;
            align-items: center;
            justify-content: space-between;
            margin-bottom: 6px;
        }

        .btn-regen {
            background: transparent;
            color: #ef4444;
            border: 1px solid #ef444466;
            border-radius: 6px;
            padding: 4px 12px;
            font-size: 12px;
            cursor: pointer;
            transition: background 0.2s, border-color 0.2s;
        }

        .btn-regen:hover:not(:disabled) {
            background: #ef444422;
            border-color: #ef4444;
        }

        .btn-regen:disabled {
            opacity: 0.4;
            cursor: not-allowed;
        }

        .section-label {
            font-size: 13px;
            font-weight: 600;
            color: #e5e7eb;
            margin-bottom: 6px;
        }

        .junk-hint {
            font-size: 11px;
            color: #6b7280;
            margin-bottom: 10px;
        }

        .junk-fields {
            display: flex;
            flex-direction: column;
            gap: 8px;
            margin-bottom: 10px;
        }

        .junk-row {
            display: grid;
            grid-template-columns: 28px 1fr;
            gap: 8px;
            align-items: start;
        }

        .junk-label {
            font-size: 12px;
            font-weight: 600;
            color: #9ca3af;
            padding-top: 6px;
        }

        .junk-ta {
            background: #0b1120;
            border: 1px solid #1f2937;
            border-radius: 6px;
            padding: 6px 10px;
            color: #d1fae5;
            font-family: 'Courier New', monospace;
            font-size: 10px;
            width: 100%;
            resize: vertical;
            word-break: break-all;
        }

        .junk-ta:focus {
            outline: none;
            border-color: #ef4444;
        }

        .junk-fixed {
            font-size: 11px;
            color: #6b7280;
            background: #0b1120;
            border: 1px solid #1f2937;
            border-radius: 6px;
            padding: 6px 10px;
            line-height: 1.6;
        }

        .fixed-badge {
            background: #ef444422;
            color: #ef4444;
            border: 1px solid #ef444444;
            border-radius: 4px;
            padding: 1px 6px;
            font-size: 10px;
            font-weight: 600;
            margin-right: 4px;
        }
    </style>
</head>
<body>
    <div class="top-bar">
        <div class="top-bar-content">
            <div class="title">MeizFL's RedWARP Generator</div>
        </div>
    </div>

    <div class="container">
        <div class="main-content">
            <!-- Left card -->
            <div class="card">
                <div class="card-header">Connection Settings</div>
                <div class="card-description">Endpoint, MTU and AmneziaWG parameters for RedWARP.</div>
                
                <div class="form-group">
                    <div class="form-row">
                        <label for="endpoint">Endpoint:</label>
                        <input type="text" id="endpoint" value="51.38.153.32:5242">
                    </div>
                </div>

                <div class="form-group">
                    <div class="form-row">
                        <label for="mtu">MTU:</label>
                        <input type="text" id="mtu" value="1340" style="width: 120px;">
                    </div>
                </div>

                <div class="form-group">
                    <div class="form-row">
                        <label for="amnezia">AmneziaWG:</label>
                        <select id="amnezia" style="width: 120px;" onchange="updateAmneziaUI()">
                            <option value="true">Yes</option>
                            <option value="false">No</option>
                        </select>
                    </div>
                </div>

                <div id="junkSection">
                    <div class="divider"></div>
                    <div class="junk-header">
                        <div class="section-label">Junk Packets (I1–I5)</div>
                        <button class="btn-regen" id="regenBtn" onclick="regeneratePackets()">🔀 Regenerate</button>
                    </div>
                    <div class="junk-hint">Raw packets injected before handshake in <code style="color:#9ca3af">&lt;b 0x...&gt;</code> format. See <a href="https://voidwaifu.github.io/Special-Junk-Packet-List/" target="_blank" style="color:#ef4444;">Special Junk Packet List</a>.</div>
                    <div class="junk-fields">
                        <div class="junk-row"><label class="junk-label">I1</label><textarea id="i1" class="junk-ta" rows="2" spellcheck="false"><b 0x5245474953544552207369703a676f6f676c652e636f6d205349502f322e300d0a5669613a205349502f322e302f554450203139322e3136382e3231372e3136303a353036303b6272616e63683d7a39684734624b3163386233656234343039353564336537353364373736660d0a4d61782d466f7277617264733a2037300d0a546f3a203c7369703a7573657240676f6f676c652e636f6d3e0d0a46726f6d3a203c7369703a7573657240676f6f676c652e636f6d3e3b7461673d343135366435303337313166393932650d0a43616c6c2d49443a2062613963626636653930393464613863663035363038663936616163383862650d0a435365713a20312052454749535445520d0a436f6e746163743a203c7369703a75736572403139322e3136382e38362e33333a353036303e0d0a557365722d4167656e743a204272696120352e302e300d0a457870697265733a20353632350d0a436f6e74656e742d4c656e6774683a20300d0a0d0a></textarea></div>
                        <div class="junk-row"><label class="junk-label">I2</label><textarea id="i2" class="junk-ta" rows="2" spellcheck="false"><b 0x1603030065010000610303fd3f9662525abfef489a1d125b0fd6e6c4662615c04eaf75be845ec92f4b82780b527d66aeeb04a27a5650b60002cca80100002b0000001000000e00000b796f75747562652e636f6d000b000403000102000a000a00087b88652ce46b47ab></textarea></div>
                        <div class="junk-row"><label class="junk-label">I3</label><textarea id="i3" class="junk-ta" rows="2" spellcheck="false"><b 0x16030300380200003403031556c4103c94301523f867deb89d617ef2dba24104e80eb8c0329dc077488c700c0f3a6b69d048145d5bf3329b1303000000></textarea></div>
                        <div class="junk-row"><label class="junk-label">I4</label><textarea id="i4" class="junk-ta" rows="2" spellcheck="false"><b 0x100000807a6fb1d5ae519ce8092bc12e8a369a80d696caa52f5e586a235940a43d426ac1b72c644724bff2ac179907137dc285d663f2cebaa35b781f746135d4581cdbe94c23e64249b31b1eacb0196f705396ac898642c73789b9d78b7215a09419980ed5cb58533d77214cf6be24c31847bc6b3bfa9835644c991a2e989a28f16dd13014030300010116030300345d12a535259f00219af357c4ebe69d03863afe8aebaa1e57bd1e5f2bfd074ff99dd3b73e59badbdd0f076c2c1f61f2b4c00a627c></textarea></div>
                        <div class="junk-row"><label class="junk-label">I5</label><textarea id="i5" class="junk-ta" rows="2" spellcheck="false"><b 0x170303015e474554202f6d61696c20485454502f312e310d0a486f73743a207777772e676f6f676c652e636f6d0d0a557365722d4167656e743a204d6f7a696c6c612f352e30202857696e646f7773204e542031302e303b2057696e36343b2078363429204170706c655765624b69742f3533372e333620284b48544d4c2c206c696b65204765636b6f29204368726f6d652f39312e302e343437322e313234205361666172692f3533372e33360d0a4163636570743a20746578742f68746d6c2c6170706c69636174696f6e2f7868746d6c2b786d6c2c6170706c69636174696f6e2f786d6c3b713d302e392c696d6167652f776562702c2a2f2a3b713d302e380d0a4163636570742d4c616e67756167653a20656e2d55532c656e3b713d302e350d0a4163636570742d456e636f64696e673a20677a69702c206465666c6174652c2062720d0a436f6e6e656374696f6e3a206b6565702d616c6976650d0a0d0a></textarea></div>
                    </div>
                    <div class="junk-fixed">
                        <span class="fixed-badge">Fixed</span> Jc=4 · Jmin=40 · Jmax=70 · H1=1 · H2=2 · H3=random(2073986817–2147128180)
                    </div>
                </div>
            </div>

            <!-- Right card -->
            <div class="card">
                <div class="card-header">IPv6, DNS & Generation</div>
                <div class="card-description">Enable IPv6, choose DNS for IPv4/IPv6 — then click "Generate".</div>
                
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
                    <div id="status" class="status">Ready to generate.</div>
                    <button id="generateBtn" class="btn" onclick="generate()">Generate</button>
                </div>

                <div id="downloadSection" class="download-section">
                    <button id="downloadBtn" class="btn btn-download" onclick="downloadConfig()">📥 Download RedWARP.conf</button>
                </div>

                <div id="configOutput" class="config-output"></div>
            </div>
        </div>
    </div>

    <div class="footer">
        © 2025 MeizFL • RedWARP UI (ASP.NET Core F#) • Auto-download wgcf
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
            
            statusEl.textContent = '⏳ Running wgcf on server, please wait...';
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
                        dnsV4: getDnsV4(),
                        dnsV6: getDnsV6(),
                        i1: document.getElementById('i1').value,
                        i2: document.getElementById('i2').value,
                        i3: document.getElementById('i3').value,
                        i4: document.getElementById('i4').value,
                        i5: document.getElementById('i5').value
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
                statusEl.textContent = '❌ Error: ' + error.message;
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

        function updateAmneziaUI() {
            const enabled = document.getElementById('amnezia').value === 'true';
            document.getElementById('junkSection').style.display = enabled ? '' : 'none';
        }

        async function regeneratePackets() {
            const btn = document.getElementById('regenBtn');
            btn.disabled = true;
            btn.textContent = '⏳ Generating...';
            try {
                const res = await fetch('/api/packets');
                const data = await res.json();
                document.getElementById('i1').value = data.i1;
                document.getElementById('i2').value = data.i2;
                document.getElementById('i3').value = data.i3;
                document.getElementById('i4').value = data.i4;
                document.getElementById('i5').value = data.i5;
            } catch(e) {
                alert('Failed to generate packets: ' + e.message);
            } finally {
                btn.disabled = false;
                btn.textContent = '🔀 Regenerate';
            }
        }

        updateIpv6UI();
        updateAmneziaUI();
        regeneratePackets();
    </script>
</body>
</html>
"""

// ===== Main Application =====
[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    let app = builder.Build()

    // Create directories
    let workDir = Path.Combine(Directory.GetCurrentDirectory(), "work")
    Directory.CreateDirectory(workDir) |> ignore

    let binDir = Path.Combine(Directory.GetCurrentDirectory(), "bin")
    Directory.CreateDirectory(binDir) |> ignore

    app.UseStaticFiles() |> ignore

    // Home page
    app.MapGet("/", Func<IResult>(fun () -> 
        Results.Content(getHtmlPage(), "text/html")
    )) |> ignore

    // API endpoint for packet generation preview
    app.MapGet("/api/packets", Func<IResult>(fun () ->
        let pkts = generateJunkPackets()
        Results.Json({|
            i1 = pkts.I1
            i2 = pkts.I2
            i3 = pkts.I3
            i4 = pkts.I4
            i5 = pkts.I5
        |})
    )) |> ignore

    // API endpoint for config generation
    app.MapPost("/api/generate", Func<GenerateRequest, Task<IResult>>(fun req -> task {
        // Auto-generate packets if Amnezia is enabled and fields are empty
        let req =
            if req.AmneziaEnabled && String.IsNullOrWhiteSpace(req.I1) then
                let pkts = generateJunkPackets()
                { req with I1 = pkts.I1; I2 = pkts.I2; I3 = pkts.I3; I4 = pkts.I4; I5 = pkts.I5 }
            else req

        let sessionId = Guid.NewGuid().ToString("N")
        let sessionDir = Path.Combine(workDir, sessionId)
        Directory.CreateDirectory(sessionDir) |> ignore

        try
            // Ensure wgcf exists or download it
            let! wgcfPath = ensureWgcfExists() |> Async.StartAsTask
            
            match wgcfPath with
            | None ->
                return Results.Json({|
                    success = false
                    message = "Failed to find or download wgcf. Check your internet connection."
                |})
            | Some wgcfPath ->
                // Run wgcf register
                if not (runCommand wgcfPath sessionDir ["register"; "--accept-tos"]) then
                    return Results.Json({|
                        success = false
                        message = "Command execution error: wgcf register"
                    |})
                else
                    // Run wgcf generate
                    if not (runCommand wgcfPath sessionDir ["generate"]) then
                        return Results.Json({|
                            success = false
                            message = "Command execution error: wgcf generate"
                        |})
                    else
                        let profilePath = Path.Combine(sessionDir, "wgcf-profile.conf")
                        if not (File.Exists(profilePath)) then
                            return Results.Json({|
                                success = false
                                message = "wgcf-profile.conf not found after generate"
                            |})
                        else
                            // Process the config
                            let outputPath = Path.Combine(sessionDir, "RedWARP.conf")
                            do! processConfigFile profilePath outputPath req |> Async.StartAsTask

                            if not (File.Exists(outputPath)) then
                                return Results.Json({|
                                    success = false
                                    message = "RedWARP.conf was not created"
                                |})
                            else
                                // Read the final config
                                let! configContent = File.ReadAllTextAsync(outputPath)
                                
                                // Clean up temporary files
                                try Directory.Delete(sessionDir, true) with _ -> ()

                                return Results.Json({|
                                    success = true
                                    message = "Config successfully generated!"
                                    config = configContent
                                    filename = "RedWARP.conf"
                                |})
        with ex ->
            // Clean up temp folder on error
            try Directory.Delete(sessionDir, true) with _ -> ()
            return Results.Json({|
                success = false
                message = "Error: " + ex.Message
            |})
    })) |> ignore

    app.Run()
    0
