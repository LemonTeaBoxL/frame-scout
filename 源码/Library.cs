using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
namespace FrameScout {
 public sealed partial class Shell {
  readonly HashSet<string> roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  List<VideoItem> forcedQueue;bool refreshing,sortDescending;readonly SemaphoreSlim metadataGate=new SemaphoreSlim(2);
  void SetupLibrary() {
   Click("RefreshFolder",async(s,e)=>await RefreshLibrary());Click("ScanChanged",(s,e)=> {if(scanCancel!=null)return;forcedQueue=items.Where(x=>x.Changed&&File.Exists(x.Path)).ToList();BeginScan();});
   Get<ComboBox>("VideoSort").SelectionChanged+=(s,e)=>ApplySort();Get<ComboBox>("VideoSort").SelectedIndex=0;Click("SortDirection",(s,e)=> {sortDescending=!sortDescending;Get<Button>("SortDirection").Content=sortDescending?"↓":"↑";ApplySort();});
   videos.AddHandler(Button.ClickEvent,new RoutedEventHandler((s,e)=> {var b=e.OriginalSource as Button;if(b!=null&&(b.Tag as string)=="Review") {var item=b.DataContext as VideoItem;if(item!=null){item.CycleReview();ApplySort();}e.Handled=true;}}));
  }
  void ApplySort() {if(videos==null||videos.ItemsSource==null)return;var view=CollectionViewSource.GetDefaultView(videos.ItemsSource) as ListCollectionView;int i=Get<ComboBox>("VideoSort").SelectedIndex;if(view==null)return;using(view.DeferRefresh()) {view.CustomSort=null;view.SortDescriptions.Clear();view.CustomSort=new VideoValueComparer(Math.Max(0,Math.Min(6,i)),sortDescending);}foreach(var col in videos.Columns)col.SortDirection=col.SortMemberPath==VideoValueComparer.Keys[Math.Max(0,Math.Min(6,i))]?(sortDescending?ListSortDirection.Descending:ListSortDirection.Ascending):(ListSortDirection?)null;}
  public static string Fingerprint(string file) {using(var sha=SHA256.Create())using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var ms=new MemoryStream()) {var buf=new byte[65536];foreach(long at in new[]{0L,Math.Max(0,stream.Length/2-32768),Math.Max(0,stream.Length-65536)}) {stream.Position=at;int n=stream.Read(buf,0,buf.Length);ms.Write(buf,0,n);}return BitConverter.ToString(sha.ComputeHash(ms.ToArray())).Replace("-","");}}
  async void LoadMetadata(IEnumerable<VideoItem> source) {foreach(var item in source.ToList()) {await metadataGate.WaitAsync();try {var data=await Task.Run(()=> {var fi=new FileInfo(item.Path);return new {Info=item.Info??Detector.Probe(item.Path,CancellationToken.None),Size=fi.Length,Ticks=fi.LastWriteTimeUtc.Ticks,Hash=Fingerprint(item.Path)};});if(closing)return;item.Info=data.Info;item.FileSize=data.Size;item.ModifiedTicks=data.Ticks;item.Fingerprint=data.Hash;item.Refresh();}catch {try {var fi=new FileInfo(item.Path);item.ModifiedTicks=fi.LastWriteTimeUtc.Ticks;item.Refresh();}catch {}}finally {metadataGate.Release();}}if(!closing)ApplySort();}
  async Task RefreshLibrary() {Diagnostics.Log("library.refresh_start",new{Folders=roots.ToArray()});
   if(refreshing||scanCancel!=null) {statusText.Text="请先停止当前检测，再刷新视频文件夹。";return;}refreshing=true;Get<Button>("RefreshFolder").IsEnabled=false;statusText.Text="正在刷新文件夹并比较文件变化…";
   var old=items.ToList();var folders=roots.ToList();try {
    var snapshots=await Task.Run(()=> {var paths=new HashSet<string>(old.Select(x=>x.Path),StringComparer.OrdinalIgnoreCase);var stack=new Stack<string>(folders);while(stack.Count>0) {var d=stack.Pop();try {if(!Directory.Exists(d)||(File.GetAttributes(d)&FileAttributes.ReparsePoint)!=0)continue;foreach(var f in Directory.GetFiles(d))if(extensions.Contains(Path.GetExtension(f)))paths.Add(Path.GetFullPath(f));foreach(var sub in Directory.GetDirectories(d))stack.Push(sub);}catch {}}var list=new List<VideoItem>();foreach(var p in paths)try {if(!File.Exists(p))continue;var fi=new FileInfo(p);list.Add(new VideoItem {Path=p,FileSize=fi.Length,ModifiedTicks=fi.LastWriteTimeUtc.Ticks,Fingerprint=Fingerprint(p)});}catch(Exception ex) {if(File.Exists(p))list.Add(new VideoItem {Path=p,Error="暂时无法读取："+ex.Message,Status="暂不可读"});}return list;});
    if(closing)return;var map=old.ToDictionary(x=>x.Path,StringComparer.OrdinalIgnoreCase);var exists=new HashSet<string>(snapshots.Select(x=>x.Path),StringComparer.OrdinalIgnoreCase);int added=0,changed=0,removed=0;var fresh=new List<VideoItem>();foreach(var shot in snapshots) {VideoItem item;if(shot.Error!=null) {if(map.TryGetValue(shot.Path,out item)) {item.Result=null;item.ManualReview=null;item.Changed=true;item.Error=shot.Error;item.Status="暂不可读";item.Refresh();}else {items.Add(shot);added++;}continue;}if(!map.TryGetValue(shot.Path,out item)) {items.Add(shot);fresh.Add(shot);added++;}else if(item.FileSize!=shot.FileSize||item.ModifiedTicks!=shot.ModifiedTicks||item.Fingerprint!=shot.Fingerprint) {item.Result=null;item.Info=null;item.Error=null;item.ManualReview=null;item.Changed=true;item.Status="文件已变更";item.FileSize=shot.FileSize;item.ModifiedTicks=shot.ModifiedTicks;item.Fingerprint=shot.Fingerprint;item.Refresh();fresh.Add(item);changed++;}}
    foreach(var item in old)if(!exists.Contains(item.Path)) {items.Remove(item);removed++;}UpdateList();LoadMetadata(fresh);if(current!=null&&fresh.Contains(current))SelectVideo(current);Diagnostics.Log("library.refreshed",new{Added=added,Changed=changed,Removed=removed});statusText.Text="刷新完成：新增 "+added+"，变更 "+changed+"，移出不存在文件 "+removed+"；可点击“检测最新的视频”。";ApplySort();
   }catch(Exception ex) {Diagnostics.Error("library.refresh_error",ex);statusText.Text="刷新未完成："+ex.Message;}finally {refreshing=false;Get<Button>("RefreshFolder").IsEnabled=true;}
  }
 }
}
