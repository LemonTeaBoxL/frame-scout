using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FrameScout {
 public static class Program {
  [STAThread] public static int Main(string[] args) {
   if(args.Length>=3&&args[0]=="--analyze") {
    try {var options=new Settings();if(args.Length>3) {int value;options.SensitivityValue=int.TryParse(args[3],out value)?Math.Max(0,Math.Min(100,value)):args[3]=="灵敏"?80:args[3]=="保守"?10:50;}if(args.Length>4)Detector.DiagnosticFile=args[4];var r=Detector.Analyze(args[1],options,null,CancellationToken.None);File.WriteAllText(args[2],Util.Json().Serialize(r),new UTF8Encoding(false));return 0;}
    catch(Exception e) {File.WriteAllText(args[2],Util.Json().Serialize(new {Error=e.ToString()}));return 1;}
   }
   var app=new Application();app.ShutdownMode=ShutdownMode.OnMainWindowClose;
   app.DispatcherUnhandledException+=(s,e)=> {try {Directory.CreateDirectory(Path.GetDirectoryName(Settings.FilePath));File.AppendAllText(Path.Combine(Path.GetDirectoryName(Settings.FilePath),"error.log"),DateTime.Now+" "+e.Exception+Environment.NewLine);}catch {}MessageBox.Show("操作未完成："+e.Exception.Message,"视频巡检");e.Handled=true;};
   try {var shell=new Shell();if(args.Length>0)shell.ImportAfterLoad(args);app.Run(shell.Window);return 0;}
   catch(Exception e) {File.WriteAllText(Path.Combine(Path.GetTempPath(),"FrameScout-startup-error.txt"),e.ToString());MessageBox.Show(e.Message,"视频巡检启动失败");return 1;}
  }
 }
 public sealed partial class Shell {
  public Window Window;
  readonly ObservableCollection<VideoItem> items=new ObservableCollection<VideoItem>();
  readonly Settings saved=Settings.Load();
  DataGrid videos,issues;MediaElement player;Image still;Slider timeline;Canvas markers;
  ComboBox workers,speed;Slider sensitivity;TextBox blackMin;CheckBox checkInsert,checkJump,checkBlack;
  TextBlock currentName,videoMeta,clock,frameLabel,previewMessage,statusText,issueTitle,listCount;
  ProgressBar overall;FrameworkElement emptyHint;Button start,stop,play;
  CancellationTokenSource scanCancel,previewCancel,selectCancel;VideoItem current;
  DispatcherTimer timer;bool playing,mediaOpened,updating,seeking,closing;double position,duration,replayEnd=-1;int selectionVersion;
  int completed,total,active;readonly Dictionary<VideoItem,int> percentages=new Dictionary<VideoItem,int>();
  static readonly HashSet<string> extensions=new HashSet<string>(new[]{".mp4",".mov",".mkv",".avi",".m4v",".webm",".mts",".m2ts",".ts",".mpg",".mpeg",".flv",".wmv",".ogv",".mxf",".vob"},StringComparer.OrdinalIgnoreCase);
  T Get<T>(string name) where T:class {return Window.FindName(name) as T;}
  void Click(string name,RoutedEventHandler action) {Get<Button>(name).Click+=action;}
  public Shell() {
   using(var stream=typeof(Program).Assembly.GetManifestResourceStream("MainWindow.xaml"))Window=(Window)XamlReader.Load(stream);
   var area=SystemParameters.WorkArea;Window.Width=Math.Min(1680,area.Width*0.95);Window.Height=Math.Min(960,area.Height*0.94);Window.MinWidth=Math.Min(1060,area.Width*0.9);Window.MinHeight=Math.Min(680,area.Height*0.9);
   try {using(var s=typeof(Program).Assembly.GetManifestResourceStream("app.ico")) {var decoder=new IconBitmapDecoder(s,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);Window.Icon=decoder.Frames[decoder.Frames.Count-1];Get<Image>("BrandIcon").Source=Window.Icon;}}catch {}
   videos=Get<DataGrid>("Videos");issues=Get<DataGrid>("Issues");player=Get<MediaElement>("Player");still=Get<Image>("Still");timeline=Get<Slider>("Timeline");markers=Get<Canvas>("Markers");workers=Get<ComboBox>("Workers");sensitivity=Get<Slider>("Sensitivity");speed=Get<ComboBox>("Speed");blackMin=Get<TextBox>("BlackMin");checkInsert=Get<CheckBox>("CheckInsert");checkJump=Get<CheckBox>("CheckJump");checkBlack=Get<CheckBox>("CheckBlack");currentName=Get<TextBlock>("CurrentName");videoMeta=Get<TextBlock>("VideoMeta");clock=Get<TextBlock>("Clock");frameLabel=Get<TextBlock>("FrameLabel");previewMessage=Get<TextBlock>("PreviewMessage");statusText=Get<TextBlock>("StatusText");issueTitle=Get<TextBlock>("IssueTitle");listCount=Get<TextBlock>("ListCount");overall=Get<ProgressBar>("Overall");emptyHint=Get<FrameworkElement>("EmptyHint");start=Get<Button>("StartScan");stop=Get<Button>("StopScan");play=Get<Button>("PlayPause");
   int max=Math.Max(1,Environment.ProcessorCount);for(int i=1;i<=max;i++)workers.Items.Add(i);workers.SelectedItem=Math.Max(1,Math.Min(max,saved.Workers));Get<TextBlock>("CpuInfo").Text="/ "+max+" 逻辑核";
   sensitivity.Value=saved.SensitivityValue;blackMin.Text=saved.BlackMin.ToString("0.###",Util.CI);checkInsert.IsChecked=saved.DetectInsert;checkJump.IsChecked=saved.DetectJump;checkBlack.IsChecked=saved.DetectBlack;Get<CheckBox>("CheckSingle").IsChecked=saved.DetectSingleJump;Get<TextBox>("ClipMaxFrames").Text=saved.ClipMaxFrames.ToString();Get<CheckBox>("SkipClipTail").IsChecked=saved.SkipClipTail;Get<TextBox>("ClipTailFrames").Text=saved.ClipTailFrames.ToString();Get<TextBox>("BlackEdge").Text=saved.BlackEdgeFrames.ToString();Get<CheckBox>("BlackMiddle").IsChecked=saved.BlackMiddle;
   workers.ToolTip="同时检测的视频数量。每个视频使用一个分析工作线程，解码线程限制为 1。下一轮检测生效。";videos.ItemsSource=items;
   videos.SelectionChanged+=(s,e)=>SelectVideo(videos.SelectedItem as VideoItem);
   SetupLibrary();
   var subtitles=new SubtitlePanel(Window,()=>items.Select(x=>x.Path).ToList());Get<ContentControl>("SubtitleHost").Content=subtitles.View;
   Get<TabControl>("MainTabs").SelectionChanged+=(s,e)=> {if(e.OriginalSource!=Get<TabControl>("MainTabs"))return;bool video=Get<TabControl>("MainTabs").SelectedIndex==0;Get<Border>("VideoToolbar").Visibility=video?Visibility.Visible:Visibility.Collapsed;if(!video)Pause(false);else subtitles.Pause();};
   issues.SelectionChanged+=(s,e)=> {var f=issues.SelectedItem as Finding;if(f!=null)Seek(f.Start,true);};
   videos.PreviewMouseLeftButtonUp+=(s,e)=> {var row=ItemsControl.ContainerFromElement(videos,e.OriginalSource as DependencyObject) as DataGridRow;if(row==null||Keyboard.Modifiers!=ModifierKeys.None)return;var item=row.Item as VideoItem;if(item==current&&item!=null&&item.Result!=null&&item.Result.Findings.Count>0) {issues.SelectedIndex=0;if(Math.Abs(position-item.Result.Findings[0].Start)>0.001||playing)Seek(item.Result.Findings[0].Start,true);}};
   issues.PreviewMouseLeftButtonUp+=(s,e)=> {var row=ItemsControl.ContainerFromElement(issues,e.OriginalSource as DependencyObject) as DataGridRow;var f=row==null?null:row.Item as Finding;if(f!=null&&(Math.Abs(position-f.Start)>0.001||playing))Seek(f.Start,true);};
   Click("AddFiles",(s,e)=> {var d=new OpenFileDialog {Title="添加视频（可多选）",Multiselect=true,Filter="视频文件|*.mp4;*.mov;*.mkv;*.avi;*.m4v;*.webm;*.mts;*.m2ts;*.ts;*.mpg;*.mpeg;*.flv;*.wmv;*.ogv;*.mxf;*.vob|所有文件|*.*"};if(d.ShowDialog(Window)==true)Import(d.FileNames);});
   Click("AddFolder",(s,e)=> {using(var d=new System.Windows.Forms.FolderBrowserDialog {Description="选择文件夹，将包含子文件夹中的视频",ShowNewFolderButton=false})if(d.ShowDialog()==System.Windows.Forms.DialogResult.OK)Import(new[]{d.SelectedPath});});
   Click("StartScan",(s,e)=>BeginScan());Click("StopScan",(s,e)=> {if(scanCancel!=null) {scanCancel.Cancel();stop.IsEnabled=false;statusText.Text="正在停止检测，保留已完成的视频结果…";}});
   Click("Remove",(s,e)=> {if(scanCancel!=null)return;foreach(var item in videos.SelectedItems.Cast<VideoItem>().ToList())items.Remove(item);UpdateList();});
   Click("Clear",(s,e)=> {if(scanCancel!=null)return;items.Clear();roots.Clear();UpdateList();});
   Click("Export",(s,e)=>Export());Click("Help",(s,e)=>ShowHelp());
   Click("PlayPause",(s,e)=> {if(playing)Pause(true);else {replayEnd=-1;Play(position);}});
   Click("PreviousFrame",(s,e)=>Step(-1));Click("NextFrame",(s,e)=>Step(1));Click("PreviousIssue",(s,e)=>MoveIssue(-1));Click("NextIssue",(s,e)=>MoveIssue(1));
   Click("ReplayIssue",(s,e)=> {var f=issues.SelectedItem as Finding;if(f==null&&current!=null&&current.Result!=null)f=current.Result.Findings.FirstOrDefault();if(f==null)return;replayEnd=Math.Min(duration,f.End+1);Play(Math.Max(0,f.Start-1));});
   speed.SelectionChanged+=(s,e)=> {player.SpeedRatio=new[]{0.5,1.0,1.5,2.0}[Math.Max(0,speed.SelectedIndex)];};
   player.MediaOpened+=(s,e)=> {mediaOpened=true;revealAfter=DateTime.UtcNow.AddMilliseconds(150);if(player.NaturalDuration.HasTimeSpan&&duration<=0) {duration=player.NaturalDuration.TimeSpan.TotalSeconds;timeline.Maximum=Math.Max(0.01,duration);}player.Position=TimeSpan.FromSeconds(position);if(playing)player.Play();else player.Close();};
   player.MediaEnded+=(s,e)=> {Pause(true);replayEnd=-1;};
   player.MediaFailed+=(s,e)=> {playing=false;play.Content="▶ 播放";still.Visibility=Visibility.Visible;statusText.Text="系统播放器不支持此编码；仍可点击异常或使用前后帧查看画面。";};
   timeline.PreviewMouseLeftButtonDown+=(s,e)=> {seeking=true;Pause(false);};
   timeline.PreviewMouseLeftButtonUp+=(s,e)=> {seeking=false;Seek(timeline.Value,true);};
   timeline.LostMouseCapture+=(s,e)=> {if(seeking) {seeking=false;Seek(timeline.Value,true);}};
   timeline.PreviewKeyUp+=(s,e)=> {if(!updating)Seek(timeline.Value,true);};markers.SizeChanged+=(s,e)=>DrawMarkers();
   Window.PreviewDragOver+=(s,e)=> {e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};
   Window.Drop+=(s,e)=> {if(e.Data.GetDataPresent(DataFormats.FileDrop)) {var paths=(string[])e.Data.GetData(DataFormats.FileDrop);if(Get<TabControl>("MainTabs").SelectedIndex==1)subtitles.Import(paths);else Import(paths);}e.Handled=true;};
   Window.PreviewKeyDown+=(s,e)=> {if(Get<TabControl>("MainTabs").SelectedIndex!=0)return;if(e.OriginalSource is TextBox||e.OriginalSource is ComboBox||e.OriginalSource is StableSplitter)return;if(e.Key==Key.Space) {if(playing)Pause(true);else Play(position);e.Handled=true;}if(e.Key==Key.Left) {Step(-1);e.Handled=true;}if(e.Key==Key.Right) {Step(1);e.Handled=true;}};
   Window.Closing+=(s,e)=> {closing=true;playing=false;mediaOpened=false;revealing=false;subtitles.Close();SaveSettings();if(scanCancel!=null)scanCancel.Cancel();if(previewCancel!=null)previewCancel.Cancel();if(selectCancel!=null)selectCancel.Cancel();timer.Stop();player.Close();};
   timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(80)};timer.Tick+=(s,e)=> {if(playing&&mediaOpened&&!seeking) {position=player.Position.TotalSeconds;UpdateClock();if(replayEnd>=0&&position>=replayEnd) {replayEnd=-1;Pause(true);}}};timer.Start();SetupEnhancements(subtitles);SetupWorkspace();
  }
  public void ImportAfterLoad(string[] paths) {Window.Loaded+=(s,e)=>Import(paths);}
  Settings ReadSettings() {
   double b;if(!double.TryParse(blackMin.Text.Trim(),System.Globalization.NumberStyles.Float,Util.CI,out b)||double.IsNaN(b)||double.IsInfinity(b)||b<0||b>60)throw new ArgumentException("黑屏最短时间请输入 0 至 60 秒的小数，例如 0.04。填 0 可报告单帧黑屏。");
   int tail;if(!int.TryParse(Get<TextBox>("ClipTailFrames").Text,out tail)||tail<0||tail>100000)throw new ArgumentException("尾部跳过帧数请输入 0 至 100000，默认 60。");int clipMax;if(!int.TryParse(Get<TextBox>("ClipMaxFrames").Text,out clipMax)||clipMax<1||clipMax>3000)throw new ArgumentException("夹帧持续帧数上限请输入 1 至 3000，默认 4。");int edge;if(!int.TryParse(Get<TextBox>("BlackEdge").Text,out edge)||edge<0||edge>100000)throw new ArgumentException("黑屏边缘检测帧数请输入 0 至 100000。");return new Settings {SkipClipTail=Get<CheckBox>("SkipClipTail").IsChecked==true,ClipTailFrames=tail,ClipMaxFrames=clipMax,BlackEdgeFrames=edge,BlackMiddle=Get<CheckBox>("BlackMiddle").IsChecked==true,Workers=workers.SelectedItem is int?(int)workers.SelectedItem:1,SensitivityValue=(int)sensitivity.Value,DetectSingleJump=false,BlackMin=b,DetectInsert=checkInsert.IsChecked==true,DetectJump=false,DetectBlack=checkBlack.IsChecked==true};
  }
  void SaveSettings() {try {ReadSettings().Save();}catch {}}
  async void Import(string[] paths) {
   foreach(var p in paths)if(Directory.Exists(p))roots.Add(Path.GetFullPath(p));
   statusText.Text="正在查找视频…";int skipped=0;
   var found=await Task.Run(()=> {var files=new List<string>();var pending=new Stack<string>(paths);while(pending.Count>0) {var p=pending.Pop();try {if(File.Exists(p)) {if(extensions.Contains(Path.GetExtension(p)))files.Add(Path.GetFullPath(p));else skipped++;}else if(Directory.Exists(p)) {if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)continue;foreach(var f in Directory.GetFiles(p))pending.Push(f);foreach(var d in Directory.GetDirectories(p))pending.Push(d);}else skipped++;}catch {skipped++;}}return files;});
   if(closing)return;int added=0;var fresh=new List<VideoItem>();var known=new HashSet<string>(items.Select(x=>x.Path),StringComparer.OrdinalIgnoreCase);foreach(var p in found.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))if(known.Add(p)) {var item=new VideoItem {Path=p};items.Add(item);fresh.Add(item);added++;}UpdateList();statusText.Text="已添加 "+added+" 个视频"+(skipped>0?"；跳过 "+skipped+" 个非视频或不可读取项目":"")+"。";LoadMetadata(fresh);if(videos.SelectedItem==null&&items.Count>0)videos.SelectedIndex=0;
  }
  void UpdateList() {listCount.Text=items.Count+" 个视频";emptyHint.Visibility=items.Count==0?Visibility.Visible:Visibility.Collapsed;if(subtitlePanel!=null)subtitlePanel.RefreshVideoLinks(false);}
  async void BeginScan() {
   if(scanCancel!=null)return;if(items.Count==0) {statusText.Text="请先添加视频或文件夹。";return;}
   Settings options;try {options=ReadSettings();}catch(Exception e) {MessageBox.Show(Window,e.Message,"检测设置");return;}
   if(!options.DetectInsert&&!options.DetectJump&&!options.DetectBlack&&!options.DetectSingleJump) {MessageBox.Show(Window,"请至少选择一种检测类型。","检测设置");return;}
   var selected=videos.SelectedItems.Cast<VideoItem>().ToList();var queue=forcedQueue??(selected.Count>1?selected:items.Where(x=>x.Result==null).ToList());bool forced=forcedQueue!=null;forcedQueue=null;
   if(queue.Count==0&&forced) {statusText.Text="刷新后没有新增或变更的视频需要检测。";return;}if(queue.Count==0)queue=selected.Count==1?selected:items.ToList();
   scanCancel=new CancellationTokenSource();var token=scanCancel.Token;completed=0;active=0;total=queue.Count;percentages.Clear();foreach(var item in queue) {item.Result=null;item.ManualReview=null;item.Error=null;item.Status="排队中";item.Refresh();percentages[item]=0;}
   if(current!=null&&queue.Contains(current)) {issues.ItemsSource=null;DrawMarkers();}
   start.IsEnabled=false;stop.IsEnabled=true;Get<Button>("Remove").IsEnabled=false;Get<Button>("Clear").IsEnabled=false;workers.IsEnabled=false;options.Save();int next=-1;
   var tasks=Enumerable.Range(0,Math.Min(options.Workers,queue.Count)).Select(worker=>Task.Run(()=> {
    while(!token.IsCancellationRequested) {int index=Interlocked.Increment(ref next);if(index>=queue.Count)break;var item=queue[index];
     Window.Dispatcher.Invoke(new Action(()=> {item.Running=true;item.Status="等待文件稳定…";active++;UpdateScanStatus();}));
     try {WaitForStableFile(item.Path,token).GetAwaiter().GetResult();var result=Detector.Analyze(item.Path,options,p=>Window.Dispatcher.BeginInvoke(new Action(()=> {if(closing||!item.Running||item.Result!=null)return;percentages[item]=p;item.Status="检测中 "+p+"%";UpdateScanStatus();})),token);
      Window.Dispatcher.Invoke(new Action(()=> {item.Result=result;item.ManualReview=null;item.Info=result.Info;item.Changed=false;item.Status=result.Findings.Count>0?"发现疑点":result.CheckedInsert&&result.ClipCheckedFrames==0?"夹帧范围为空":"未发现异常";item.Refresh();if(item==current)RefreshResult();}));
     }catch(OperationCanceledException) {Window.Dispatcher.Invoke(new Action(()=> {item.Status="已停止";item.Refresh();}));}
     catch(Exception ex) {Window.Dispatcher.Invoke(new Action(()=> {item.Status="检测失败";item.Error=ex.Message;item.Refresh();if(item==current)issueTitle.Text=ex.Message;}));}
     finally {Window.Dispatcher.Invoke(new Action(()=> {item.Running=false;active--;if(!token.IsCancellationRequested) {completed++;percentages[item]=100;}UpdateScanStatus();}));}
    }
   })).ToArray();
   try {await Task.WhenAll(tasks);}catch(Exception e) {if(!closing)statusText.Text="检测中断："+e.Message;}
   if(closing)return;bool cancelled=token.IsCancellationRequested;foreach(var item in queue)if(item.Status=="排队中")item.Status="待检测";scanCancel.Dispose();scanCancel=null;start.IsEnabled=true;stop.IsEnabled=false;Get<Button>("Remove").IsEnabled=true;Get<Button>("Clear").IsEnabled=true;workers.IsEnabled=true;
   ApplySort();int failed=queue.Count(x=>x.Status=="检测失败");statusText.Text=cancelled?"检测已停止，已完成结果可以继续查看。":"检测完成："+queue.Count(x=>x.Result!=null)+" / "+total+" 个视频，"+queue.Where(x=>x.Result!=null).Sum(x=>x.Result.Findings.Count)+" 处疑点"+(failed>0?"，"+failed+" 个失败（选中查看原因）":"")+"。";
  }
  void UpdateScanStatus() {if(closing)return;overall.Value=total>0?percentages.Values.Sum()/total:0;statusText.Text="逐帧检测中  ·  已完成 "+completed+" / "+total+"  ·  同时处理 "+active+" 个视频";}
  async void SelectVideo(VideoItem item) {
   int version=++selectionVersion;if(selectCancel!=null)selectCancel.Cancel();selectCancel=new CancellationTokenSource();selectCancel.CancelAfter(20000);var token=selectCancel.Token;
   Pause(false);if(previewCancel!=null)previewCancel.Cancel();player.Close();still.Source=null;still.Visibility=Visibility.Visible;current=item;position=0;duration=0;replayEnd=-1;issues.ItemsSource=null;DrawMarkers();UpdateClock();
   if(item==null) {currentName.Text="画面预览";videoMeta.Text="选中视频后，在这里检查可疑画面";previewMessage.Text="视频预览";previewMessage.Visibility=Visibility.Visible;return;}
   currentName.Text=item.Name;currentName.ToolTip=item.Path;videoMeta.Text="读取视频信息…";previewMessage.Text="正在加载画面…";previewMessage.Visibility=Visibility.Visible;
   try {var info=item.Info??await Task.Run(()=>Detector.Probe(item.Path,token));if(version!=selectionVersion||closing)return;item.Info=info;duration=info.Duration;timeline.Maximum=Math.Max(0.01,duration);videoMeta.Text=info.Width+" × "+info.Height+"  ·  "+info.Fps.ToString("0.###",Util.CI)+" fps  ·  "+info.Codec+"  ·  "+Util.Time(duration);
    player.Volume=0.65;RefreshResult();double target=item.Result!=null&&item.Result.Findings.Count>0?item.Result.Findings[0].Start:0;if(requestedVideo!=null&&requestedVideo.Equals(item.Path,StringComparison.OrdinalIgnoreCase)){target=requestedTime;requestedVideo=null;}Seek(target,true);
   }catch(OperationCanceledException) {if(version==selectionVersion&&!closing)previewMessage.Text="读取超时，选择其他视频或重试。";}
   catch(Exception ex) {if(version==selectionVersion&&!closing) {videoMeta.Text="无法读取视频";previewMessage.Text=ex.Message;issueTitle.Text=item.Error??ex.Message;}}
  }
  void RefreshResult() {
   if(current==null)return;issues.ItemsSource=current.Result==null?null:current.Result.Findings;issueTitle.Text=current.Result==null?(current.Error??"异常明细  ·  检测后显示，可点击定位"):current.Result.Findings.Count==0?"本次启用的规则未发现异常":"异常明细  ·  "+current.Result.Findings.Count+" 处，点击定位";
   if(current.Result!=null&&current.Result.Notice.Length>0)issueTitle.Text+="  ·  "+current.Result.Notice;
   if(current.Info!=null) {duration=current.Info.Duration;timeline.Maximum=Math.Max(0.01,duration);}DrawMarkers();UpdateClock();
  }
  int FrameAt(double t) {if(current==null)return 0;if(current.Result!=null&&current.Result.Times.Count>0) {var times=current.Result.Times;int i=times.BinarySearch(t);if(i<0)i=Math.Max(0,~i-1);return Math.Min(times.Count-1,i);}return (int)Math.Round(t*(current.Info==null?25:current.Info.Fps));}
  void UpdateClock() {clock.Text=Util.Time(position)+" / "+Util.Time(duration);frameLabel.Text=current==null?"—":"第 "+(FrameAt(position)+1)+" 帧"+(current.Result==null?"（估算）":"");updating=true;timeline.Value=Math.Max(0,Math.Min(timeline.Maximum,position));updating=false;}
  async void Seek(double time,bool preview) {
   if(current==null||current.Info==null||closing)return;Pause(false);position=Math.Max(0,Math.Min(Math.Max(0,duration-0.001),time));try {player.Position=TimeSpan.FromSeconds(position);}catch {}UpdateClock();if(!preview)return;
   if(previewCancel!=null)previewCancel.Cancel();previewCancel=new CancellationTokenSource();previewCancel.CancelAfter(20000);var token=previewCancel.Token;var file=current.Path;double target=position;still.Visibility=Visibility.Visible;previewMessage.Text="读取当前帧…";previewMessage.Visibility=Visibility.Visible;
   try {var data=await Task.Run(()=>Detector.Preview(file,target,token));if(token.IsCancellationRequested||closing||current==null||current.Path!=file)return;var bitmap=new BitmapImage();using(var ms=new MemoryStream(data)) {bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=ms;bitmap.EndInit();bitmap.Freeze();}still.Source=bitmap;previewMessage.Visibility=Visibility.Collapsed;}
   catch(OperationCanceledException) {if(!closing&&previewCancel!=null&&previewCancel.Token==token)previewMessage.Text="读取已停止，点击时间线可重试。";}
   catch(Exception ex) {if(!closing&&!token.IsCancellationRequested) {previewMessage.Text="该时间点无法显示，请尝试前后帧。";statusText.Text=ex.Message;}}
  }
  void Pause(bool preview) {if(current==null)return;if(playing&&mediaOpened)position=player.Position.TotalSeconds;playing=false;revealing=false;mediaOpened=false;try {player.Close();}catch {}play.Content="▶ 播放";UpdateClock();if(preview)Seek(position,true);}
  void Play(double time) {if(current==null||current.Info==null)return;if(previewCancel!=null)previewCancel.Cancel();position=Math.Max(0,Math.Min(duration,time));if(position>=duration-0.01)position=0;still.Visibility=Visibility.Visible;revealStartPosition=position;revealing=true;previewMessage.Visibility=still.Source==null?Visibility.Visible:Visibility.Collapsed;playing=true;mediaOpened=false;play.Content="Ⅱ 暂停";player.Source=new Uri(current.Path);player.SpeedRatio=new[]{0.5,1.0,1.5,2.0}[Math.Max(0,speed.SelectedIndex)];player.Play();}
  void Step(int delta) {if(current==null||current.Info==null)return;Pause(false);int index=FrameAt(position);double t;if(current.Result!=null&&current.Result.Times.Count>0) {var times=current.Result.Times;index=Math.Max(0,Math.Min(times.Count-1,index+delta));t=times[index];}else t=Math.Max(0,position+delta/current.Info.Fps);Seek(t,true);}
  void MoveIssue(int delta) {if(current==null||current.Result==null||current.Result.Findings.Count==0)return;int i=issues.SelectedIndex;if(i<0)i=delta>0?-1:current.Result.Findings.Count;issues.SelectedIndex=Math.Max(0,Math.Min(current.Result.Findings.Count-1,i+delta));issues.ScrollIntoView(issues.SelectedItem);}
  void DrawMarkers() {markers.Children.Clear();if(current==null||current.Result==null||duration<=0)return;timeline.ApplyTemplate();var track=timeline.Template.FindName("PART_Track",timeline) as System.Windows.Controls.Primitives.Track;double left=0,width=markers.ActualWidth;if(track!=null) {double thumb=track.Thumb==null?0:track.Thumb.ActualWidth;left=track.TranslatePoint(new Point(thumb/2,0),markers).X;width=Math.Max(1,track.ActualWidth-thumb);}foreach(var finding in current.Result.Findings) {var f=finding;var mark=new Border {Width=Math.Max(3,(f.End-f.Start)/duration*width),Height=13,Background=(Brush)new BrushConverter().ConvertFromString(f.Kind=="黑屏"?"#A2AFC5":f.Kind=="夹帧"?"#F5AA62":"#EB707E"),Cursor=Cursors.Hand,ToolTip=f.Kind+" · "+f.TimeText+"\n"+f.Detail};Canvas.SetLeft(mark,left+f.Start/duration*width);mark.MouseLeftButtonDown+=(sender,e)=> {issues.SelectedItem=f;issues.ScrollIntoView(f);Seek(f.Start,true);e.Handled=true;};markers.Children.Add(mark);}}
  void Export() {if(!items.Any(x=>x.Result!=null)) {statusText.Text="还没有可导出的检测结果。";return;}var d=new SaveFileDialog {Title="导出检测结果",Filter="CSV 表格（Excel 可打开）|*.csv|完整 JSON 报告|*.json",FileName="视频巡检-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")};if(d.ShowDialog(Window)!=true)return;try {WriteReport(d.FileName,items);statusText.Text="结果已导出："+d.FileName;}catch(Exception e) {MessageBox.Show(Window,"导出失败："+e.Message,"视频巡检");}}
  public static void WriteReport(string file,IEnumerable<VideoItem> source) {if(Path.GetExtension(file).Equals(".json",StringComparison.OrdinalIgnoreCase)) {File.WriteAllText(file,Util.Json().Serialize(source.Select(x=>new {x.Path,x.Status,x.Error,x.Review,x.ManualReview,x.ModifiedText,x.Changed,Result=x.Result})),new UTF8Encoding(false));return;}var sb=new StringBuilder("视频,完整路径,检测状态,夹帧,黑屏,类型,判断,开始时间,结束时间,帧号,结束帧号,具体情况,审核状态\r\n");foreach(var item in source) {string review=item.Review==0?"通过":item.Review==1?"有错":item.Review==2?"存疑":"待审核";var findings=item.Result==null?new List<Finding>():item.Result.Findings;if(findings.Count==0)AppendCsv(sb,new[]{item.Name,item.Path,item.Status,item.Insert,item.Black,"","","","","","",item.Error??item.Summary,review});else foreach(var f in findings)AppendCsv(sb,new[]{item.Name,item.Path,item.Status,item.Insert,item.Black,f.Kind,f.Level,Util.Time(f.Start),Util.Time(f.End),(f.Frame+1).ToString(),(f.EndFrame+1).ToString(),f.Detail,review});}File.WriteAllText(file,sb.ToString(),new UTF8Encoding(true));}
  static void AppendCsv(StringBuilder sb,string[] cells) {sb.AppendLine(string.Join(",",cells.Select(s=>"\""+(s??"").Replace("\"","\"\"")+"\"")));}
  void ShowHelp() {string file=Path.Combine(Util.BaseDir,"使用说明.txt");if(File.Exists(file)){var w=FrameWindows.Dialog(Window,"视频巡检 v1.4.1 · 使用说明",820,720);w.Content=new TextBox {Text=File.ReadAllText(file),IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(14)};w.Show();}else MessageBox.Show(Window,"拖入视频后检测夹帧与黑屏。灵敏度 0 最严格，100 最灵敏；夹帧最多持续帧数默认 4，黑屏默认只检查前后各 15 帧；夹帧默认跳过最后有效画面之前的 60 帧。右键视频可添加、移除、单独检测或用默认应用打开；预览区右上角箭头可悬浮与还原。右键状态栏管理列。字幕页可导入 SRT、关联视频、分组校对并查看日志。","视频巡检 v1.4.1");}
 }
}
