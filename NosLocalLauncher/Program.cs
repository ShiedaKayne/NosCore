using System.Diagnostics;
using System.Text;

const string ClientDir = @"D:\Nostale";
const string ClientExe = "NostaleClientX.exe";
const string PatchedExe = "NosCore.exe";

var clientPath = Path.Combine(ClientDir, ClientExe);
var patchedPath = Path.Combine(ClientDir, PatchedExe);

Console.WriteLine("=== NosCore Local Launcher ===");
Console.WriteLine();

// Step 1: Patch the client binary
Console.WriteLine("[1/2] Patching client binary...");

if (!File.Exists(clientPath))
{
    Console.WriteLine($"  ERROR: Client not found at {clientPath}");
    return 1;
}

var bytes = File.ReadAllBytes(clientPath);

var ipResult = PatchServerAddress(bytes, "127.0.0.1");
Console.WriteLine($"  IP patch: {(ipResult.Success ? "OK" : "FAILED")}");
foreach (var line in ipResult.Log.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    Console.WriteLine($"    {line.TrimEnd()}");

var entwellResult = PatchForceEntwell(bytes);
Console.WriteLine($"  Force-Entwell: {(entwellResult.Success ? "OK" : "FAILED")}");
if (!entwellResult.Success)
    Console.WriteLine($"    {entwellResult.Log}");

if (!ipResult.Success)
{
    Console.WriteLine("  ERROR: IP patch failed. Cannot continue.");
    return 1;
}

File.WriteAllBytes(patchedPath, bytes);
Console.WriteLine($"  Written: {patchedPath}");

// Step 2: Launch patched client in Entwell (standalone) mode
Console.WriteLine();
Console.WriteLine("[2/2] Launching client (Entwell mode)...");
Console.WriteLine("  The game login screen will appear. Log in with: admin / test");

var psi = new ProcessStartInfo
{
    FileName = patchedPath,
    WorkingDirectory = ClientDir,
    UseShellExecute = true,
    Verb = "runas",
};

Process? process;
try
{
    process = Process.Start(psi);
}
catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
{
    Console.WriteLine("  ERROR: UAC elevation was cancelled.");
    return 1;
}

if (process == null)
{
    Console.WriteLine("  ERROR: Failed to start client process.");
    return 1;
}

Console.WriteLine($"  Client started (PID: {process.Id}).");
Console.WriteLine("  Waiting for client to exit...");
Console.CancelKeyPress += (_, e) => { e.Cancel = true; };

await process.WaitForExitAsync();
Console.WriteLine($"  Client exited with code {process.ExitCode}.");
return 0;


// ===== Binary patching (from NosCore.DeveloperTools ClientPatcher.cs) =====

static PatchResult PatchServerAddress(byte[] bytes, string newAddress)
{
    if (newAddress.Length > 15) return new(false, "Address too long.");

    var candidates = FindIpShapedAnsiStrings(bytes);
    if (candidates.Count == 0) return new(false, "No IP-shaped Delphi AnsiString found.");

    var sb = new StringBuilder();
    var replacement = Encoding.ASCII.GetBytes(newAddress);

    foreach (var (payloadOffset, declaredLength, currentValue) in candidates)
    {
        if (replacement.Length > declaredLength)
        {
            sb.AppendLine($"Skip 0x{payloadOffset:X} ('{currentValue}'): won't fit.");
            continue;
        }

        for (var i = 0; i < replacement.Length; i++)
            bytes[payloadOffset + i] = replacement[i];
        for (var i = replacement.Length; i < declaredLength; i++)
            bytes[payloadOffset + i] = 0x00;
        WriteInt32LE(bytes, payloadOffset - 4, replacement.Length);

        sb.AppendLine($"Patched 0x{payloadOffset:X}: '{currentValue}' -> '{newAddress}'");
    }

    return new(true, sb.ToString());
}

static List<(int PayloadOffset, int DeclaredLength, string CurrentValue)> FindIpShapedAnsiStrings(byte[] bytes)
{
    var results = new List<(int, int, string)>();
    for (var i = 0; i <= bytes.Length - 20; i++)
    {
        if (bytes[i] != 0xFF || bytes[i + 1] != 0xFF || bytes[i + 2] != 0xFF || bytes[i + 3] != 0xFF) continue;
        var len = BitConverter.ToInt32(bytes, i + 4);
        if (len < 7 || len > 15) continue;
        var payloadStart = i + 8;
        if (payloadStart + len > bytes.Length) continue;

        var end = payloadStart + len;
        while (end > payloadStart && bytes[end - 1] == 0) end--;
        var trimmed = end - payloadStart;
        if (trimmed < 7) continue;

        var ok = true;
        var dots = 0;
        for (var k = payloadStart; k < payloadStart + trimmed; k++)
        {
            var b = bytes[k];
            if (b == '.') { dots++; continue; }
            if (b is >= (byte)'0' and <= (byte)'9') continue;
            ok = false;
            break;
        }
        if (!ok || dots != 3) continue;

        var value = Encoding.ASCII.GetString(bytes, payloadStart, trimmed);
        results.Add((payloadStart, len, value));
    }
    return results;
}

/// <summary>
/// Force the client into the Entwell standalone body unconditionally.
/// Replaces the early argc JL with an unconditional JMP to the Entwell body.
/// The client shows its own login screen — no GF launcher chain needed.
/// </summary>
static PatchResult PatchForceEntwell(byte[] bytes)
{
    const string entwellPattern =
        "B9 14 00 00 00 BA 01 00 00 00 E8 ? ? ? ? 8B 45 CC BA ? ? ? ? E8 ? ? ? ? 0F 85";
    const string argcPattern = "E8 ? ? ? ? E8 ? ? ? ? 48 0F 8C";

    var entwellBlock = FindPattern(bytes, entwellPattern, 0);
    if (entwellBlock < 0)
        return new(false, "Entwell anchor pattern not found.");

    var argcBlock = FindPattern(bytes, argcPattern, 0);
    if (argcBlock < 0)
        return new(false, "Argc-gate pattern not found.");

    var entwellBody = entwellBlock + 34;
    var jlOffset = argcBlock + 11;
    var rel32 = entwellBody - (jlOffset + 5);
    bytes[jlOffset + 0] = 0xE9;
    bytes[jlOffset + 1] = (byte)rel32;
    bytes[jlOffset + 2] = (byte)(rel32 >> 8);
    bytes[jlOffset + 3] = (byte)(rel32 >> 16);
    bytes[jlOffset + 4] = (byte)(rel32 >> 24);
    bytes[jlOffset + 5] = 0x90;

    return new(true, $"JMP at 0x{jlOffset:X} -> Entwell body 0x{entwellBody:X}");
}

static int FindPattern(byte[] haystack, string pattern, int startOffset)
{
    var tokens = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var patternBytes = new byte[tokens.Length];
    var mask = new bool[tokens.Length];
    for (var i = 0; i < tokens.Length; i++)
    {
        if (tokens[i] is "?" or "??") { mask[i] = false; }
        else { mask[i] = true; patternBytes[i] = Convert.ToByte(tokens[i], 16); }
    }

    for (var i = startOffset; i <= haystack.Length - patternBytes.Length; i++)
    {
        var match = true;
        for (var j = 0; j < patternBytes.Length; j++)
        {
            if (mask[j] && haystack[i + j] != patternBytes[j]) { match = false; break; }
        }
        if (match) return i;
    }
    return -1;
}

static void WriteInt32LE(byte[] dst, int offset, int value)
{
    dst[offset + 0] = (byte)value;
    dst[offset + 1] = (byte)(value >> 8);
    dst[offset + 2] = (byte)(value >> 16);
    dst[offset + 3] = (byte)(value >> 24);
}

record PatchResult(bool Success, string Log);
