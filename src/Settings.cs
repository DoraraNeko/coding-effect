using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodingEffect;

internal sealed class Settings
{
    /// <summary>エフェクトの有効/無効</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Cyan / Rainbow / Fire / Sakura / Matrix</summary>
    public string Theme { get; set; } = "Cyan";

    /// <summary>パーティクル量の倍率（0.2〜5）</summary>
    public double Intensity { get; set; } = 1.0;

    /// <summary>コンボ数の表示</summary>
    public bool Combo { get; set; } = true;

    /// <summary>この連打数からコンボを表示</summary>
    public int ComboMin { get; set; } = 5;

    /// <summary>Backspace / Delete で炎を出す</summary>
    public bool FireOnDelete { get; set; } = true;

    /// <summary>キャレット位置が取れないときにマウス位置で代用する</summary>
    public bool MouseFallback { get; set; } = true;

    /// <summary>キー押下からキャレット位置を読むまでの待ち時間（ms）。アプリ側でキャレットが動くのを待つ。</summary>
    public int CaretDelayMs { get; set; } = 25;

    /// <summary>エフェクトを出さないプロセス名（例: "valorant", "obs64"）</summary>
    public List<string> ExcludedProcesses { get; set; } = new();

    public static readonly (string Id, string Label)[] Themes =
    {
        ("Cyan", "シアン"),
        ("Rainbow", "レインボー"),
        ("Fire", "ファイア"),
        ("Sakura", "サクラ"),
        ("Matrix", "マトリックス"),
    };

    [JsonIgnore]
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodingEffect", "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOpts) ?? new Settings();
        }
        catch { /* 壊れていたら既定値 */ }
        var s = new Settings();
        s.Save();
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch { }
    }

    public void CopyFrom(Settings o)
    {
        Enabled = o.Enabled;
        Theme = o.Theme;
        Intensity = o.Intensity;
        Combo = o.Combo;
        ComboMin = o.ComboMin;
        FireOnDelete = o.FireOnDelete;
        MouseFallback = o.MouseFallback;
        CaretDelayMs = o.CaretDelayMs;
        ExcludedProcesses = o.ExcludedProcesses ?? new();
    }
}
