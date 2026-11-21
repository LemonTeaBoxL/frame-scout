using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace FrameScout {
 public sealed partial class Shell {
  DockablePanel videoDock;bool resumeVideoAfterMove;double videoMovePosition;string videoMovePath;
  void SetupVideoDock() {videoDock=new DockablePanel(Window,Get<FrameworkElement>("VideoPreviewPane"),Get<Button>("FloatVideo"),"视频预览",BeforeVideoMove,AfterVideoMove,e=> {if(e.OriginalSource is TextBox||e.OriginalSource is ComboBox)return;if(e.Key==Key.Space){if(playing)Pause(true);else Play(position);e.Handled=true;}else if(e.Key==Key.Left){Step(-1);e.Handled=true;}else if(e.Key==Key.Right){Step(1);e.Handled=true;}},paths=>Import(paths));}
  async Task BeforeVideoMove() {resumeVideoAfterMove=playing;Pause(false);videoMovePosition=position;videoMovePath=current==null?null:current.Path;if(resumeVideoAfterMove&&current!=null){if(previewCancel!=null)previewCancel.Cancel();try{var bytes=await Task.Run(()=>Detector.Preview(videoMovePath,videoMovePosition,CancellationToken.None));if(!closing){still.Source=FrameWindows.Bitmap(bytes);still.Visibility=Visibility.Visible;previewMessage.Visibility=Visibility.Collapsed;}}catch {}}}
  void AfterVideoMove() {DrawMarkers();if(resumeVideoAfterMove&&!closing&&current!=null&&current.Path==videoMovePath)Play(videoMovePosition);}
  public static ProcessStartInfo DefaultVideoOpenInfo(string file) {return new ProcessStartInfo {FileName=file,UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(file)};}
  void OpenDefaultVideo(VideoItem item) {if(item==null)return;try{if(!extensions.Contains(Path.GetExtension(item.Path)))throw new IOException("仅支持使用默认应用打开视频文件。");if(!File.Exists(item.Path))throw new FileNotFoundException("视频文件不存在，请刷新列表。");if(item==current&&playing)Pause(true);Process.Start(DefaultVideoOpenInfo(item.Path));statusText.Text="已使用系统默认应用打开："+item.Name;}catch(Exception ex){statusText.Text="打开视频失败："+ex.Message;}}
 }
 public sealed partial class SubtitlePanel {
  DockablePanel subtitleDock;bool resumeSubtitleAfterMove;double subtitleMovePosition,subtitleMoveEnd;string subtitleMovePath;
  void SetupSubtitleDock() {subtitleDock=new DockablePanel(owner,Get<FrameworkElement>("SubtitlePreviewPane"),Get<Button>("FloatSubtitle"),"字幕视频预览",BeforeSubtitleMove,AfterSubtitleMove,e=> {if(e.OriginalSource is TextBox||e.OriginalSource is ComboBox)return;if(e.Key==Key.Space){if(playing)Pause();else Get<Button>("PlayCue").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));e.Handled=true;}},paths=>Import(paths));}
  async Task BeforeSubtitleMove() {resumeSubtitleAfterMove=playing;subtitleMovePosition=Get<MediaElement>("SubPlayer").Position.TotalSeconds;subtitleMoveEnd=playEnd;subtitleMovePath=current==null?null:current.VideoPath;playing=false;subRevealing=false;Get<MediaElement>("SubPlayer").Close();if(resumeSubtitleAfterMove)await PreviewAt(subtitleMovePosition);}
  void AfterSubtitleMove() {if(resumeSubtitleAfterMove&&!closed&&current!=null&&current.VideoPath==subtitleMovePath){playStart=subtitleMovePosition;playEnd=subtitleMoveEnd;playing=true;subRevealing=true;subRevealAfter=DateTime.MaxValue;Get<Image>("SubStill").Visibility=Visibility.Visible;var player=Get<MediaElement>("SubPlayer");player.Source=new Uri(current.VideoPath);player.Play();}}
 }
}
