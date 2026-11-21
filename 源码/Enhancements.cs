using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
namespace FrameScout {
 public sealed class NaturalVideoComparer : IComparer {
  readonly bool reverse;public NaturalVideoComparer(bool reverse) {this.reverse=reverse;}
  public int Compare(object a,object b) {int c=CompareNames(((VideoItem)a).Name,((VideoItem)b).Name);if(c==0)c=StringComparer.OrdinalIgnoreCase.Compare(((VideoItem)a).Path,((VideoItem)b).Path);return reverse?-c:c;}
  public static int CompareNames(string a,string b) {int i=0,j=0;while(i<a.Length&&j<b.Length) {bool da=a[i]>='0'&&a[i]<='9',db=b[j]>='0'&&b[j]<='9';if(da&&db) {int ai=i,bi=j;while(i<a.Length&&char.IsDigit(a[i]))i++;while(j<b.Length&&char.IsDigit(b[j]))j++;int az=ai,bz=bi;while(az<i-1&&a[az]=='0')az++;while(bz<j-1&&b[bz]=='0')bz++;int c=(i-az).CompareTo(j-bz);if(c==0)c=string.Compare(a,az,b,bz,i-az,StringComparison.Ordinal);if(c!=0)return c;int zeros=(i-ai).CompareTo(j-bi);if(zeros!=0)return zeros;}else {int c=char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));if(c!=0)return c;i++;j++;}}return (a.Length-i).CompareTo(b.Length-j);}
 }
 public sealed class NaturalSubtitleComparer : IComparer {
  readonly bool reverse;public NaturalSubtitleComparer(bool reverse=false){this.reverse=reverse;}
  public int Compare(object a,object b){int c=NaturalVideoComparer.CompareNames(((SubtitleFile)a).Name,((SubtitleFile)b).Name);return reverse?-c:c;}
 }
 public sealed partial class Shell {
  SubtitlePanel subtitlePanel;bool revealing;DateTime revealAfter;double revealStartPosition;VideoItem contextItem;
  void SetupEnhancements(SubtitlePanel panel) {
   SetupVideoDock();subtitlePanel=panel;panel.OpenVideoRequested+=OpenLinkedVideo;foreach(var col in videos.Columns)if((string)col.Header=="出现位置"||(string)col.Header=="出现情况")col.Visibility=Visibility.Collapsed;
   videos.PreviewMouseRightButtonDown+=(s,e)=> {var row=ItemsControl.ContainerFromElement(videos,e.OriginalSource as DependencyObject) as DataGridRow;contextItem=row==null?null:row.Item as VideoItem;if(contextItem!=null&&!videos.SelectedItems.Contains(contextItem))videos.SelectedItem=contextItem;};
   var menu=new ContextMenu();var add=new MenuItem {Header="添加视频…"};add.Click+=(s,e)=>Get<Button>("AddFiles").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));menu.Items.Add(add);var folder=new MenuItem {Header="添加文件夹…"};folder.Click+=(s,e)=>Get<Button>("AddFolder").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));menu.Items.Add(folder);menu.Items.Add(new Separator());var remove=new MenuItem {Header="删除此视频（从列表移除）",ToolTip="仅移除列表项目，保留原视频文件"};remove.Click+=(s,e)=> {var item=contextItem??videos.SelectedItem as VideoItem;if(item!=null&&scanCancel==null){items.Remove(item);contextItem=null;UpdateList();QueueSave();}};menu.Items.Add(remove);var openDefault=new MenuItem {Header="使用默认应用打开"};openDefault.Click+=(s,e)=>OpenDefaultVideo(contextItem??videos.SelectedItem as VideoItem);menu.Items.Add(openDefault);menu.Items.Add(new Separator());var single=new MenuItem {Header="检测此视频"};single.Click+=(s,e)=> {var item=contextItem??videos.SelectedItem as VideoItem;if(item!=null&&scanCancel==null) {forcedQueue=new List<VideoItem>{item};BeginScan();}};menu.Items.Add(single);
   var multi=new MenuItem {Header="检测选中视频"};multi.Click+=(s,e)=> {if(scanCancel==null){forcedQueue=videos.SelectedItems.Cast<VideoItem>().ToList();BeginScan();}};menu.Items.Add(multi);menu.Items.Add(new Separator());
   foreach(var state in new[]{0,1,2}) {int value=state;var m=new MenuItem {Header=Review.Icon(value)+" 标记为"+(value==0?"通过":value==1?"有错":"存疑")};m.Click+=(s,e)=> {var item=contextItem??videos.SelectedItem as VideoItem;if(item!=null){item.ManualReview=value;item.Refresh();ApplySort();QueueSave();}};menu.Items.Add(m);}
   menu.Items.Add(new Separator());var columnMenu=new MenuItem {Header="显示 / 隐藏列",ToolTip="点击打开列选择面板"};columnMenu.Click+=(s,e)=>ShowColumnChooser();menu.Items.Add(columnMenu);menu.Opened+=(s,e)=> {var target=contextItem??videos.SelectedItem as VideoItem;remove.IsEnabled=scanCancel==null&&target!=null;openDefault.IsEnabled=target!=null&&System.IO.File.Exists(target.Path);single.IsEnabled=scanCancel==null&&(contextItem??videos.SelectedItem as VideoItem)!=null;multi.IsEnabled=scanCancel==null&&videos.SelectedItems.Count>0;};videos.ContextMenu=menu;videos.Sorting+=(s,e)=> {int index=Array.IndexOf(VideoValueComparer.Keys,e.Column.SortMemberPath);if(index<0)return;e.Handled=true;sortDescending=e.Column.SortDirection==ListSortDirection.Ascending;foreach(var col in videos.Columns)col.SortDirection=null;Get<ComboBox>("VideoSort").SelectedIndex=index;e.Column.SortDirection=sortDescending?ListSortDirection.Descending:ListSortDirection.Ascending;Get<Button>("SortDirection").Content=sortDescending?"↓":"↑";ApplySort();QueueSave();};
   var columnsOnly=new ContextMenu();var chooseColumns=new MenuItem{Header="显示 / 隐藏列"};chooseColumns.Click+=(s,e)=>ShowColumnChooser();columnsOnly.Items.Add(chooseColumns);statusText.ContextMenu=columnsOnly;Get<WrapPanel>("ReviewActions").ContextMenu=columnsOnly;
   Click("PreviousVideo",(s,e)=>MoveVideo(-1));Click("NextVideo",(s,e)=>MoveVideo(1));Click("ReviewGood",(s,e)=>SetCurrentReview(0));Click("ReviewBad",(s,e)=>SetCurrentReview(1));Click("ReviewUnsure",(s,e)=>SetCurrentReview(2));
   Click("CompareFrames",(s,e)=>OpenFrameCompare());Click("ZoomPreview",(s,e)=> {if(still.Source!=null)FrameWindows.Zoom(Window,still.Source,currentName.Text);});
   Click("Calibration",(s,e)=>OpenCalibration());Click("SaveProject",(s,e)=>SaveProjectAs());Click("OpenProject",(s,e)=>OpenProject());
   timeline.SizeChanged+=(s,e)=>DrawMarkers();Window.Loaded+=(s,e)=> {timeline.ApplyTemplate();DrawMarkers();};Window.PreviewKeyDown+=(s,e)=> {if(Keyboard.Modifiers==ModifierKeys.Control&&(e.Key==Key.PageDown||e.Key==Key.PageUp)){MoveVideo(e.Key==Key.PageDown?1:-1);e.Handled=true;}};
   items.CollectionChanged+=(s,e)=> {if(e.NewItems!=null)foreach(VideoItem item in e.NewItems)item.PropertyChanged+=ItemChanged;QueueSave();};panel.Changed+=QueueSave;
   timer.Tick+=(s,e)=> {if(revealing&&playing&&mediaOpened&&DateTime.UtcNow>=revealAfter&&player.Position.TotalSeconds>revealStartPosition+0.015) {revealing=false;Window.Dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(()=> {if(playing&&!revealing) {still.Visibility=Visibility.Collapsed;previewMessage.Visibility=Visibility.Collapsed;}}));}UpdateReviewLabel();};
   SetupProjectAutosave();
  }
  void SetCurrentReview(int value) {if(current==null)return;current.ManualReview=value;current.Refresh();ApplySort();UpdateReviewLabel();QueueSave();}
  void UpdateReviewLabel() {UpdateAnomalyNavigation();var t=Get<TextBlock>("PreviewReview");if(t==null)return;t.Text=current==null?"未选中视频":current.ReviewIcon+" "+(current.Review==0?"通过":current.Review==1?"有错":current.Review==2?"存疑":"待检测");var order=videos.Items.Cast<VideoItem>().ToList();int at=order.IndexOf(current);Get<Button>("PreviousVideo").IsEnabled=at>0;Get<Button>("NextVideo").IsEnabled=at>=0&&at<order.Count-1;t.Foreground=(Brush)new BrushConverter().ConvertFromString(current==null?"#8FA2BD":current.ReviewColor);}
  void ItemChanged(object sender,PropertyChangedEventArgs e) {if(e.PropertyName=="ReviewIcon"||e.PropertyName=="Meta"||e.PropertyName=="Summary")QueueSave();}
  async Task WaitForStableFile(string file,CancellationToken ct) {long lastSize=-1,lastTicks=-1;int stable=0;var until=DateTime.UtcNow.AddSeconds(45);while(true) {ct.ThrowIfCancellationRequested();try {var fi=new FileInfo(file);long size=fi.Length,ticks=fi.LastWriteTimeUtc.Ticks;if(size==lastSize&&ticks==lastTicks&&size>0)stable++;else stable=0;lastSize=size;lastTicks=ticks;using(var access=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read)) {if(stable>=2)return;}}catch(IOException) {stable=0;}if(DateTime.UtcNow>=until)throw new IOException("视频仍在写入或被独占，稍后刷新后重试。");await Task.Delay(450,ct);}}
  async void OpenFrameCompare() {if(current==null||current.Info==null)return;Pause(true);var item=current;int index=FrameAt(position);var f=issues.SelectedItem as Finding;await FrameWindows.Compare(Window,item,index,f,at=>Seek(at,true),(kind,error)=>AddCalibrationSample(item,index,kind,error));}
  string requestedVideo;double requestedTime;
  async void OpenLinkedVideo(string file,double time) {if(!File.Exists(file)){statusText.Text="关联的视频文件不存在，请重新关联。";return;}requestedVideo=file;requestedTime=time;var item=items.FirstOrDefault(x=>x.Path.Equals(file,StringComparison.OrdinalIgnoreCase));if(item==null){Import(new[]{file});for(int i=0;i<100&&item==null&&!closing;i++){await Task.Delay(50);item=items.FirstOrDefault(x=>x.Path.Equals(file,StringComparison.OrdinalIgnoreCase));}}if(item==null||closing)return;Get<TabControl>("MainTabs").SelectedIndex=0;if(videos.SelectedItem==item){if(item.Info!=null){requestedVideo=null;Seek(time,true);}else SelectVideo(item);}else videos.SelectedItem=item;videos.ScrollIntoView(item);}
 }
}
