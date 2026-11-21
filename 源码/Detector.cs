using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace FrameScout {
 public static class Detector {
  const int W=192,H=108,N=W*H;
  [ThreadStatic] public static string DiagnosticFile;
  static readonly Regex Stamp=new Regex(@"n:\s*(\d+)\s+pts:.*?pts_time:([\d.Ee+\-]+)",RegexOptions.Compiled);
  public static Process Start(string args) {
   Diagnostics.Log("ffmpeg.start",new {Arguments=args});return Process.Start(new ProcessStartInfo(Util.Engine,args) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardErrorEncoding=Encoding.UTF8 });
  }
  public static void Kill(Process p) { try { if(!p.HasExited)p.Kill(); } catch {} }
  public static VideoInfo Probe(string file,CancellationToken ct) {
   using(var p=Start("-hide_banner -nostdin -i "+Util.Q(file)))
   using(ct.Register(()=>Kill(p))) {
    var read=p.StandardError.ReadToEndAsync();
    if(!p.WaitForExit(20000)) { Kill(p); throw new IOException("读取视频信息超时"); }
    var text=read.GetAwaiter().GetResult();Diagnostics.Log("ffmpeg.probe",new {File=file,Output=text,ExitCode=p.ExitCode}); ct.ThrowIfCancellationRequested();
    var video=text.Split('\n').FirstOrDefault(l=>l.Contains("Video:")&&!l.Contains("attached pic"));
    if(video==null)throw new IOException("未找到可解码的视频轨道");
    var info=new VideoInfo();
    var d=Regex.Match(text,@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
    if(d.Success)info.Duration=int.Parse(d.Groups[1].Value)*3600+int.Parse(d.Groups[2].Value)*60+double.Parse(d.Groups[3].Value,Util.CI);
    var f=Regex.Match(video,@"([\d.]+)\s+fps"); if(f.Success)info.Fps=double.Parse(f.Groups[1].Value,Util.CI);
    if(info.Fps<0.1||info.Fps>1000)info.Fps=25;
    var wh=Regex.Match(video,@"\b(\d{2,6})x(\d{2,6})\b");
    if(wh.Success) { info.Width=int.Parse(wh.Groups[1].Value); info.Height=int.Parse(wh.Groups[2].Value); }
    var codec=Regex.Match(video,@"Video:\s*([^ ,]+)"); if(codec.Success)info.Codec=codec.Groups[1].Value;
    return info;
   }
  }
  class Metric {public double Difference,Area;public byte[] Before,After,Digest;}
  static double ImageDifference(byte[] a,byte[] b) {double sum=0;int count=0;for(int y=5;y<H-5;y++)for(int x=5;x<W-5;x++){int p=(y*W+x)*3;sum+=Math.Max(Math.Abs(a[p]-b[p]),Math.Max(Math.Abs(a[p+1]-b[p+1]),Math.Abs(a[p+2]-b[p+2])));count++;}return sum/count/255;}
  static byte[] Digest(byte[] rgb) {var data=new byte[96];int k=0;for(int gy=0;gy<4;gy++)for(int gx=0;gx<8;gx++) {long r=0,g=0,b=0;int count=0;for(int y=gy*H/4;y<(gy+1)*H/4;y+=3)for(int x=gx*W/8;x<(gx+1)*W/8;x+=3){int p=(y*W+x)*3;r+=rgb[p];g+=rgb[p+1];b+=rgb[p+2];count++;}data[k++]=(byte)(r/count);data[k++]=(byte)(g/count);data[k++]=(byte)(b/count);}return data;}
  static double DigestDiff(byte[] a,byte[] b) {double sum=0;for(int i=0;i<a.Length;i++)sum+=Math.Abs(a[i]-b[i]);return sum/a.Length/255;}
  public static AnalysisResult Analyze(string file,Settings settings,Action<int> progress,CancellationToken ct) {using(var trace=Diagnostics.BeginAnalysis(file,settings)){try{var result=AnalyzeCore(file,settings,progress,ct,trace);result.DiagnosticId=trace.Id;result.DiagnosticFolder=trace.Directory;trace.Complete(result);return result;}catch(Exception ex){trace.Fail(ex,ct.IsCancellationRequested||ex is OperationCanceledException);Diagnostics.Error("analysis.error",ex);throw;}}}
  static AnalysisResult AnalyzeCore(string file,Settings settings,Action<int> progress,CancellationToken ct,AnalysisTrace trace) {
   if(!File.Exists(Util.Engine))throw new FileNotFoundException("缺少引擎/ffmpeg.exe，请保持便携包完整。");
   var info=Probe(file,ct);var result=new AnalysisResult {Path=file,Info=info,CheckedInsert=settings.DetectInsert,CheckedBlack=settings.DetectBlack,CheckedJump=false,SettingsSignature=settings.Signature()};
   double sensitivity=Math.Max(0,Math.Min(100,settings.SensitivityValue))/100.0;double threshold=0.60-0.50*sensitivity,areaThreshold=0.95-0.40*sensitivity;int maximum=Math.Max(1,Math.Min(3000,settings.ClipMaxFrames));
   var metrics=new List<Metric>();var stamps=new List<double>();var black=new List<Tuple<int,int>>();int blackStart=-1,n=0,trailingBlackRun=0;byte[] previous=null;bool decodeError=false;var errors=new Queue<string>();
   var recent=new List<byte[]>();var restoredClips=new List<Finding>();double returnThreshold=0.30-0.18*sensitivity,returnArea=0.65-0.25*sensitivity;trace.Meta("thresholds.json",new {Sensitivity=settings.SensitivityValue,GenericDifference=threshold,GenericChangedArea=areaThreshold,ReturnDifference=returnThreshold,ReturnChangedArea=returnArea,ExactBridgeLimit=.07+.07*sensitivity,CoarseBridgeLimit=.035+.035*sensitivity,BridgeRelativeMultiplier=.22+.25*sensitivity,HeadDifference=.20-.10*sensitivity,HeadChangedArea=.55-.20*sensitivity,HeadRelativeMultiplier=6-3*sensitivity,BlackPixelMaximum=6+18*sensitivity,BlackDarkArea=.998-.048*sensitivity,BlackMeanLuminance=4+14*sensitivity,MaximumFrames=maximum,CompareWidth=W,CompareHeight=H,CropBorder=5,ChangedPixelDifference=35});
   string args="-hide_banner -nostdin -loglevel info -threads 1 -filter_threads 1 -i "+Util.Q(file)+" -map 0:V:0 -an -sn -dn -vf scale=192:108:flags=area,format=rgb24,showinfo -vsync 0 -f rawvideo pipe:1";
   using(var p=Start(args))using(ct.Register(()=>Kill(p))) {
    p.ErrorDataReceived+=(s,e)=> {if(e.Data==null)return;trace.Stderr(e.Data);var m=Stamp.Match(e.Data);if(m.Success){double t;if(double.TryParse(m.Groups[2].Value,System.Globalization.NumberStyles.Float,Util.CI,out t))lock(stamps)stamps.Add(t);}else if(!e.Data.Contains("showinfo"))lock(errors){if(errors.Count>=12)errors.Dequeue();errors.Enqueue(e.Data);if(Regex.IsMatch(e.Data,@"Error while decoding|corrupt decoded frame|Invalid data found|error submitting",RegexOptions.IgnoreCase))decodeError=true;}};p.BeginErrorReadLine();
    while(true) {ct.ThrowIfCancellationRequested();var current=new byte[N*3];int got=0;while(got<current.Length){int read=p.StandardOutput.BaseStream.Read(current,got,current.Length-got);if(read==0)break;got+=read;}ct.ThrowIfCancellationRequested();if(got==0)break;if(got!=current.Length)throw new IOException("视频帧解码不完整。");
     int dark=0,count=0,changed=0;double luminance=0,difference=0;for(int y=5;y<H-5;y++)for(int x=5;x<W-5;x++){int at=(y*W+x)*3;int r=current[at],g=current[at+1],b=current[at+2];int brightest=Math.Max(r,Math.Max(g,b));if(brightest<=6+18*sensitivity)dark++;luminance+=(r*0.2126+g*0.7152+b*0.0722);count++;if(previous!=null){int diff=Math.Max(Math.Abs(r-previous[at]),Math.Max(Math.Abs(g-previous[at+1]),Math.Abs(b-previous[at+2])));difference+=diff;if(diff>=35)changed++;}}
     bool isBlack=(double)dark/count>=0.998-0.048*sensitivity&&luminance/count<=4+14*sensitivity;var metric=new Metric {Difference=previous==null?0:difference/count/255,Area=previous==null?0:(double)changed/count,Digest=settings.DetectInsert?Digest(current):null};if(previous!=null&&metric.Difference>=threshold&&metric.Area>=areaThreshold){metric.Before=metrics[n-1].Digest;metric.After=metric.Digest;}metrics.Add(metric);recent.Add(current);trace.Frame(n,n/info.Fps,metric.Difference,metric.Area,(double)dark/count,luminance/count,isBlack,previous!=null&&metric.Difference>=threshold&&metric.Area>=areaThreshold,previous!=null&&metric.Difference>=returnThreshold&&metric.Area>=returnArea);
     if(settings.DetectInsert&&n>=2&&metric.Difference>=returnThreshold&&metric.Area>=returnArea)for(int length=1;length<=maximum&&length<n;length++){int start=n-length,before=start-1;var enter=metrics[start];if(enter.Difference<returnThreshold||enter.Area<returnArea)continue;bool exact=length<=32&&recent.Count>=length+2;double bridge=exact?ImageDifference(recent[recent.Count-length-2],current):DigestDiff(metrics[before].Digest,metric.Digest);double limit=exact?0.07+0.07*sensitivity:0.035+0.035*sensitivity;bool pass=bridge<=limit&&bridge<=Math.Min(enter.Difference,metric.Difference)*(0.22+0.25*sensitivity);trace.Decision("short_return",start,n-1,pass,pass?"进入和离开差异达标，前后画面恢复相似":"前后恢复差异未通过绝对或相对门槛",new {EntryDifference=enter.Difference,EntryArea=enter.Area,ExitDifference=metric.Difference,ExitArea=metric.Area,Bridge=bridge,BridgeLimit=limit,RelativeLimit=Math.Min(enter.Difference,metric.Difference)*(.22+.25*sensitivity),Exact=exact,Length=length});if(pass)restoredClips.Add(new Finding {Rule="short_return",Kind="夹帧",Level="疑似",Frame=start,EndFrame=n-1,Detail="短暂夹入 "+length+" 帧后恢复；进入差异 "+(enter.Difference*100).ToString("0.0",Util.CI)+"%、离开差异 "+(metric.Difference*100).ToString("0.0",Util.CI)+"%、前后恢复差异 "+(bridge*100).ToString("0.0",Util.CI)+"%"});}
     if(recent.Count>Math.Min(maximum,32)+2)recent.RemoveAt(0);trailingBlackRun=isBlack?trailingBlackRun+1:0;if(settings.DetectBlack){if(isBlack&&blackStart<0)blackStart=n;else if(!isBlack&&blackStart>=0){black.Add(Tuple.Create(blackStart,n-1));blackStart=-1;}}previous=current;n++;if(n%12==0&&progress!=null)progress(info.Duration>0?Math.Min(99,(int)(n/info.Fps/info.Duration*100)):0);
    }p.WaitForExit();ct.ThrowIfCancellationRequested();if(p.ExitCode!=0)throw new IOException("视频解码失败："+string.Join(" ",errors));
   }
   if(n==0)throw new IOException("视频没有可分析的画面。");if(decodeError)throw new IOException("视频存在解码错误，未将不完整结果标为通过。");if(blackStart>=0)black.Add(Tuple.Create(blackStart,n-1));result.FrameCount=n;
   if(stamps.Count==n){double origin=stamps[0];result.Times=stamps.Select(t=>Math.Max(0,t-origin)).ToList();}else{result.EstimatedTimes=true;result.Times=Enumerable.Range(0,n).Select(i=>i/info.Fps).ToList();result.Notice="时间点按标称帧率估算。";}
   var intervals=new List<double>();for(int i=1;i<n;i++)if(result.Times[i]>result.Times[i-1])intervals.Add(result.Times[i]-result.Times[i-1]);double dt=Util.Median(intervals);if(dt<=0)dt=1/info.Fps;info.Duration=result.Times[n-1]+dt;
   var ranges=new List<Tuple<int,int>>();foreach(var span in black) {if(settings.BlackMiddle||n<=Math.Max(0,settings.BlackEdgeFrames)*2){ranges.Add(span);continue;}int edge=Math.Max(0,settings.BlackEdgeFrames);if(edge==0)continue;if(span.Item1<edge)ranges.Add(Tuple.Create(span.Item1,Math.Min(span.Item2,edge-1)));if(span.Item2>=n-edge)ranges.Add(Tuple.Create(Math.Max(span.Item1,n-edge),span.Item2));}
   foreach(var span in ranges)result.Findings.Add(new Finding {Rule="near_black",Kind="黑屏",Level="检出",Frame=span.Item1,EndFrame=span.Item2,Detail="检测范围内的画面接近全黑；刻意黑场可人工标记通过"});
   if(settings.DetectInsert) {var boundaries=Enumerable.Range(1,Math.Max(0,n-1)).Where(i=>metrics[i].Difference>=threshold&&metrics[i].Area>=areaThreshold).ToList();var suppressed=new HashSet<int>();for(int z=0;z<boundaries.Count-1;z++){int a=boundaries[z],b=boundaries[z+1];if(b-a>maximum&&DigestDiff(metrics[a].Before,metrics[b].After)<threshold*0.45){suppressed.Add(a);suppressed.Add(b);trace.Decision("long_return_suppression",a,b-1,false,"返回型片段持续超过夹帧上限，抑制两侧普通切镜边界",new {Length=b-a,Maximum=maximum,Bridge=DigestDiff(metrics[a].Before,metrics[b].After),Limit=threshold*.45});}}boundaries=boundaries.Where(i=>!suppressed.Contains(i)).ToList();for(int k=0;k<boundaries.Count;) {ct.ThrowIfCancellationRequested();int first=boundaries[k],last=first,j=k+1;while(j<boundaries.Count&&boundaries[j]-last<=maximum){last=boundaries[j];j++;}if(last>first&&last-first<=maximum) {result.Findings.Add(new Finding {Rule="generic_dual_boundary",Kind="夹帧",Level="疑似",Frame=first,EndFrame=last-1,Detail="双侧画面差异突变，区间持续 "+(last-first)+" 帧（上限 "+maximum+" 帧）；请结合前后画面确认"});}else if(last==first){int start=first,end=first;string side="前";if(first<=maximum){start=0;end=first-1;side="后";}else if(n-first<=maximum){end=n-1;}result.Findings.Add(new Finding {Rule="single_boundary",Kind="夹帧",Level="存疑",Frame=start,EndFrame=end,Detail="单侧边界差异突变：与"+side+"侧画面差异大；当前候选 "+(end-start+1)+" 帧，普通切镜也可能触发，需查看连续帧"});}k=j;}}
   foreach(var candidate in result.Findings.Where(f=>f.Kind=="夹帧"))trace.Decision(candidate.Rule,candidate.Frame,candidate.EndFrame,true,"通用边界候选，等待合并与范围过滤",new {candidate.Detail});
   if(settings.DetectInsert){for(int length=1;length<=maximum&&length<n-2;length++){var edge=metrics[length];double headThreshold=0.20-0.10*sensitivity,headArea=0.55-0.20*sensitivity;if(edge.Difference<headThreshold||edge.Area<headArea)continue;var after=metrics.Skip(length+1).Take(8).ToList();if(after.Count<2)continue;double baseline=Util.Median(after.Select(x=>x.Difference));double adjacent=after.Take(3).Max(x=>x.Difference);double inside=length<=1?0:metrics.Skip(1).Take(length-1).Max(x=>x.Difference);bool headPass=inside<=Math.Max(0.03,edge.Difference*.3)&&adjacent<=Math.Max(0.03,edge.Difference*.35)&&edge.Difference>=Math.Max(.01,baseline)*(6-3*sensitivity);trace.Decision("head_residual",0,length-1,headPass,headPass?"片头残留后进入连续稳定画面":"片头连续性或相对变化门槛未通过",new {BoundaryDifference=edge.Difference,BoundaryArea=edge.Area,FollowingBaseline=baseline,FollowingMax=adjacent,InsideMax=inside,Length=length});if(headPass)restoredClips.Add(new Finding {Rule="head_residual",Kind="夹帧",Level="疑似",Frame=0,EndFrame=length-1,Detail="片头残帧候选：开头 "+length+" 帧后画面突变，后续画面连续；边界差异 "+(edge.Difference*100).ToString("0.0",Util.CI)+"%、变化面积 "+(edge.Area*100).ToString("0.0",Util.CI)+"%"});}
    bool skip=settings.SkipClipTail&&settings.ClipTailFrames>0;int effectiveFrames=n-trailingBlackRun;result.ClipTailBlackFrames=trailingBlackRun;result.ClipSkippedTailFrames=skip?Math.Min(effectiveFrames,Math.Max(0,settings.ClipTailFrames)):0;result.ClipCheckedFrames=skip?effectiveFrames-result.ClipSkippedTailFrames:n;int cutoff=result.ClipCheckedFrames;
    var existing=result.Findings.Where(f=>f.Kind=="夹帧"&&!restoredClips.Any(r=>f.Frame<=r.EndFrame+1&&f.EndFrame>=r.Frame-1)).ToList();var chosen=new List<Finding>();foreach(var candidate in result.Findings.Where(f=>f.Kind=="夹帧"&&!existing.Contains(f)))trace.Decision(candidate.Rule,candidate.Frame,candidate.EndFrame,false,"被相邻的短暂恢复候选替代");foreach(var clip in restoredClips.Concat(existing).OrderByDescending(f=>f.EndFrame-f.Frame)){bool inside=clip.EndFrame<cutoff,overlap=chosen.Any(x=>x.Frame<=clip.EndFrame&&clip.Frame<=x.EndFrame);trace.Decision(clip.Rule,clip.Frame,clip.EndFrame,inside&&!overlap,!inside?"候选落入有效画面尾部跳过范围":overlap?"与已保留的较长候选重叠":"通过范围与合并过滤",new {CutoffIndex=cutoff,SkippedValidFrames=result.ClipSkippedTailFrames,TrailingBlackFrames=trailingBlackRun});if(inside&&!overlap)chosen.Add(clip);}result.Findings.RemoveAll(f=>f.Kind=="夹帧");result.Findings.AddRange(chosen);
    if(skip){string note=cutoff==0?"尾部跳过已覆盖全部有效画面，夹帧未检查；可关闭此项或减小帧数。":"夹帧从最后有效画面（第 "+effectiveFrames+" 帧，"+Util.Time(result.Times[effectiveFrames-1])+"）向前跳过 "+result.ClipSkippedTailFrames+" 帧，检测截至 "+Util.Time(result.Times[cutoff])+" 前。";if(trailingBlackRun>0)note+=" 尾部连续黑屏 "+trailingBlackRun+" 帧不占用跳过帧数；黑屏仍按配置检测。";result.Notice+=(result.Notice.Length>0?" ":"")+note;}
   }
   foreach(var f in result.Findings){f.Start=result.Times[f.Frame];f.End=f.EndFrame+1<n?result.Times[f.EndFrame+1]:info.Duration;}
   foreach(var candidate in result.Findings){bool shortBlack=candidate.Kind=="黑屏"&&candidate.End-candidate.Start+.00001<settings.BlackMin;bool blackOverlap=candidate.Kind=="夹帧"&&ranges.Any(b=>candidate.Frame<=b.Item2+1&&candidate.EndFrame>=b.Item1-1);trace.Decision(candidate.Rule,candidate.Frame,candidate.EndFrame,!shortBlack&&!blackOverlap,shortBlack?"黑屏持续时间短于设置":blackOverlap?"与黑屏区间相邻或重叠，优先黑屏":"最终保留",new {candidate.Start,candidate.End,BlackMinimum=settings.BlackMin});}
   result.Findings=result.Findings.Where(f=>f.Kind!="黑屏"||f.End-f.Start+0.00001>=settings.BlackMin).Where(f=>f.Kind!="夹帧"||!ranges.Any(b=>f.Frame<=b.Item2+1&&f.EndFrame>=b.Item1-1)).OrderBy(f=>f.Frame).ThenBy(f=>f.Kind).ToList();
   if(!string.IsNullOrEmpty(DiagnosticFile))File.WriteAllText(DiagnosticFile,"frame,difference,changed_area\r\n"+string.Join("\r\n",metrics.Select((p,i)=>i+","+Util.Num(p.Difference)+","+Util.Num(p.Area))));if(progress!=null)progress(100);return result;
  }
  public static byte[] Preview(string file,double time,CancellationToken ct) {
   string args="-hide_banner -nostdin -loglevel error -threads 1 -filter_threads 1 -ss "+Util.Num(Math.Max(0,time-0.00001))+" -i "+Util.Q(file)+" -map 0:V:0 -an -sn -dn -frames:v 1 -vf scale=1280:720:force_original_aspect_ratio=decrease -f image2pipe -c:v png pipe:1";
   using(var p=Start(args))using(ct.Register(()=>Kill(p)))using(var ms=new MemoryStream()) {
    var err=p.StandardError.ReadToEndAsync();p.StandardOutput.BaseStream.CopyTo(ms);p.WaitForExit();ct.ThrowIfCancellationRequested();
    if(p.ExitCode!=0||ms.Length==0)throw new IOException("无法读取该时间点画面："+err.GetAwaiter().GetResult());return ms.ToArray();
   }
  }
 }
}
