using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace FrameScout {
 public static class Util {
  public static readonly CultureInfo CI=CultureInfo.InvariantCulture;
  public static string BaseDir=AppDomain.CurrentDomain.BaseDirectory;
  public static string Engine { get { return Path.Combine(BaseDir,"引擎","ffmpeg.exe"); } }
  public static string Q(string value) { return "\""+value.Replace("\"", "\\\"")+"\""; }
  public static string Time(double s) { var t=TimeSpan.FromSeconds(Math.Max(0,s)); return string.Format("{0:00}:{1:00}:{2:00}.{3:000}",(int)t.TotalHours,t.Minutes,t.Seconds,t.Milliseconds); }
  public static string Num(double d) { return d.ToString("0.######",CI); }
  public static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength=int.MaxValue, RecursionLimit=100 }; }
  public static double Median(IEnumerable<double> values) { var a=values.OrderBy(x=>x).ToArray(); return a.Length==0?0:a[a.Length/2]; }
 }
 public class Settings {
  public int Workers=2;
  public string Sensitivity="标准";
  public int SensitivityValue=25;
  public double BlackMin=0;
  public bool DetectInsert=true, DetectJump=false, DetectBlack=true;
  public bool DetectSingleJump=false;public int ClipMaxFrames=4;public int ClipDefaultVersion=14;
  public bool SkipClipTail=true;
  public int ClipTailFrames=60;
  public int BlackEdgeFrames=15;
  public bool BlackMiddle=false;
  public bool DiagnosticMode=false;
  public static string DataDirectory {get {string custom=Environment.GetEnvironmentVariable("FRAMESCOUT_DATA_DIR");return string.IsNullOrWhiteSpace(custom)?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FrameScout"):Path.GetFullPath(custom);}}
  public static string FilePath {get {return Path.Combine(DataDirectory,"settings.json");}}
  public static Settings Load() { try {string text=File.ReadAllText(FilePath);var s=Util.Json().Deserialize<Settings>(text)??new Settings();if(!text.Contains("SensitivityValue"))s.SensitivityValue=s.Sensitivity=="保守"?10:s.Sensitivity=="灵敏"?70:25;s.SensitivityValue=Math.Max(0,Math.Min(100,s.SensitivityValue));if(!text.Contains("\"ClipDefaultVersion\""))s.ClipMaxFrames=4;s.ClipDefaultVersion=14;return s;} catch { return new Settings { Workers=Math.Max(1,Math.Min(2,Environment.ProcessorCount/2)) }; } }
  public string Signature() {return "rules-1.4|"+SensitivityValue+"|"+Util.Num(BlackMin)+"|"+DetectInsert+"|"+ClipMaxFrames+"|"+SkipClipTail+"|"+ClipTailFrames+"|"+DetectBlack+"|"+BlackEdgeFrames+"|"+BlackMiddle;}
  public void Save() { try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)); File.WriteAllText(FilePath,Util.Json().Serialize(this)); } catch {} }
 }
 public class VideoInfo {
  public double Duration, Fps=25;
  public int Width, Height;
  public string Codec="";
 }
 public class Finding {
  public string Kind {get;set;}
  public string Level {get;set;}
  public int Frame {get;set;}
  public int EndFrame {get;set;}
  public double Start {get;set;}
  public double End {get;set;}
  public string Detail {get;set;}
  public string Rule {get;set;}
  public string TimeText {get { return Util.Time(Start); }}
  public string LengthText {get { return string.Format("{0} 帧 / {1:0.###} 秒",Math.Max(1,EndFrame-Frame+1),Math.Max(0,End-Start)); }}
  public string FrameText {get { return (Frame+1).ToString(); }}
 }
 public class AnalysisResult {
  public string Path;
  public VideoInfo Info;
  public List<Finding> Findings=new List<Finding>();
  public List<double> Times=new List<double>();
  public int FrameCount;
  public bool EstimatedTimes;
  public bool CheckedInsert, CheckedJump, CheckedBlack;
  public string Notice="";
  public string SettingsSignature;
  public string DiagnosticId,DiagnosticFolder;
  public int ClipCheckedFrames,ClipSkippedTailFrames,ClipTailBlackFrames;
 }
 public class VideoItem : INotifyPropertyChanged {
  public string Path {get;set;}
  public string Name {get { return System.IO.Path.GetFileName(Path); }}
  public string Folder {get { return System.IO.Path.GetDirectoryName(Path); }}
  public VideoInfo Info;
  public AnalysisResult Result;
  public bool Running;
  public string Error;
  public long FileSize, ModifiedTicks;
  public bool Changed=true;
  public string Fingerprint;
  public string ModifiedText {get {return ModifiedTicks==0?"—":new DateTime(ModifiedTicks,DateTimeKind.Utc).ToLocalTime().ToString("MM-dd HH:mm:ss");}}
  public int? ManualReview;
  public string Meta {get {return Info==null?"读取信息…":Util.Time(Info.Duration)+"  ·  "+Info.Width+"×"+Info.Height+"\n"+Info.Fps.ToString("0.###",Util.CI)+" fps  ·  "+(Result==null?"约 "+Math.Round(Info.Duration*Info.Fps).ToString():Result.FrameCount.ToString())+" 帧";}}
  public double DurationSeconds {get {return Info==null?-1:Info.Duration;}}
  public string DurationText {get {return Info==null?"—":Util.Time(Info.Duration);}}
  public long ResolutionPixels {get {return Info==null?-1:(long)Info.Width*Info.Height;}}
  public string ResolutionText {get {return Info==null?"—":Info.Width+"×"+Info.Height;}}
  public long FrameCountValue {get {return Result!=null?Result.FrameCount:Info==null?-1:(long)Math.Round(Info.Duration*Info.Fps);}}
  public string FrameCountText {get {return FrameCountValue<0?"—":(Result==null?"约 ":"")+FrameCountValue;}}
  public int Review {get {return ManualReview??(Result==null?-1:Result.Findings.Count==0?(Result.CheckedInsert&&Result.ClipCheckedFrames==0?2:0):Result.Findings.Any(f=>f.Kind=="黑屏")?1:2);}}
  public string ReviewIcon {get {return Review==0?"✓":Review==1?"✕":Review==2?"！":"—";}}
  public string ReviewColor {get {return Review==0?"#56D39C":Review==1?"#FF7484":Review==2?"#F3CA6C":"#8395AE";}}
  public void CycleReview() {ManualReview=Review<0?2:(Review+1)%3;Refresh();}
  string status="待检测";
  public string Status {get { return status; } set { status=value; Notify("Status"); } }
  public string Insert {get { return Count("夹帧"); }}
  public string Black {get { return Count("黑屏"); }}
  public string Jump {get { return Count("跳帧"); }}
  public string Positions {get { return Result==null?"—":Result.Findings.Count==0?"—":string.Join("、",Result.Findings.Take(3).Select(f=>f.TimeText))+(Result.Findings.Count>3?" …":""); }}
  public string Summary {get { return Result==null?(Error??"等待逐帧分析"):Result.Findings.Count==0?(Result.Notice.Length>0?Result.Notice:"本次规则未发现异常"):string.Join("；",Result.Findings.Take(3).Select(f=>f.Kind+"："+f.Detail)); }}
  string Count(string kind) { if(Result==null)return "—"; if(kind=="夹帧"&&!Result.CheckedInsert||kind=="跳帧"&&!Result.CheckedJump||kind=="黑屏"&&!Result.CheckedBlack)return "关闭"; return Result.Findings.Count(f=>f.Kind==kind).ToString(); }
  public event PropertyChangedEventHandler PropertyChanged;
  public void Notify(string property) { if(PropertyChanged!=null)PropertyChanged(this,new PropertyChangedEventArgs(property)); }
  public void Refresh() { foreach(var p in new[]{"Status","Insert","Black","Jump","Positions","Summary","Meta","ReviewIcon","ReviewColor","Review","ModifiedText","DurationSeconds","DurationText","ResolutionPixels","ResolutionText","FrameCountValue","FrameCountText"})Notify(p); }
 }
}
