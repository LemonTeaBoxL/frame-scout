using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace FrameScout {
 public sealed class DockablePanel {
  readonly Window owner;readonly FrameworkElement pane;readonly Button button;readonly string title;readonly Func<Task> beforeMove;readonly Action afterMove;readonly Action<KeyEventArgs> keyboard;readonly Action<string[]> drop;
  Panel original;int index;Border placeholder;Window floating;bool busy,ownerClosing,restoreScheduled;
  public bool IsFloating {get {return floating!=null;}}
  public DockablePanel(Window owner,FrameworkElement pane,Button button,string title,Func<Task> beforeMove,Action afterMove,Action<KeyEventArgs> keyboard=null,Action<string[]> drop=null) {
   this.owner=owner;this.pane=pane;this.button=button;this.title=title;this.beforeMove=beforeMove;this.afterMove=afterMove;this.keyboard=keyboard;this.drop=drop;
   button.Click+=async(s,e)=>await Toggle();owner.Closing+=(s,e)=> {ownerClosing=true;RestoreImmediately();};UpdateButton();
  }
  void UpdateButton(){button.Content=IsFloating?"↙":"↗";button.FontFamily=new FontFamily("Segoe UI Symbol");button.ToolTip=IsFloating?"还原到主界面":"悬浮播放窗口，可独立拖动和缩放";}
  public async Task Toggle() {if(busy||ownerClosing)return;busy=true;try {if(beforeMove!=null)await beforeMove();if(ownerClosing)return;if(IsFloating)RestoreImmediately();else Detach();UpdateButton();if(afterMove!=null&&!ownerClosing)await owner.Dispatcher.InvokeAsync(afterMove,System.Windows.Threading.DispatcherPriority.Loaded).Task;}catch(Exception ex){if(!ownerClosing)MessageBox.Show(owner,"窗口切换未完成："+ex.Message,"视频巡检");}finally{busy=false;}}
  void Detach() {
   original=pane.Parent as Panel;if(original==null)throw new InvalidOperationException("当前预览区无法分离。");index=original.Children.IndexOf(pane);
   var body=new StackPanel {VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(25)};body.Children.Add(new TextBlock {Text=title+"已悬浮",FontSize=18,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,10)});body.Children.Add(new TextBlock {Text="悬浮窗口可拖到其他位置或显示器。\n点击还原，或关闭悬浮窗口，回到此处。",TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(143,162,189)),TextAlignment=TextAlignment.Center,Margin=new Thickness(0,0,0,16)});var restore=new Button {Content="还原窗口",HorizontalAlignment=HorizontalAlignment.Center};restore.Click+=async(s,e)=>await Toggle();body.Children.Add(restore);placeholder=new Border {Background=new SolidColorBrush(Color.FromRgb(24,31,45)),CornerRadius=new CornerRadius(10),Child=body};Grid.SetColumn(placeholder,Grid.GetColumn(pane));Grid.SetRow(placeholder,Grid.GetRow(pane));Grid.SetColumnSpan(placeholder,Grid.GetColumnSpan(pane));Grid.SetRowSpan(placeholder,Grid.GetRowSpan(pane));
   double width=Math.Max(450,Math.Min(1100,pane.ActualWidth+45)),height=Math.Max(520,Math.Min(1000,pane.ActualHeight+65));original.Children.Remove(pane);original.Children.Insert(index,placeholder);
   floating=FrameWindows.Dialog(owner,"视频巡检 · "+title,(int)width,(int)height);floating.MinWidth=Math.Min(340,SystemParameters.WorkArea.Width*0.8);floating.MinHeight=Math.Min(430,SystemParameters.WorkArea.Height*0.8);floating.UseLayoutRounding=true;floating.SnapsToDevicePixels=true;floating.Icon=owner.Icon;floating.ShowInTaskbar=false;floating.Content=pane;floating.Closing+=FloatingClosing;if(keyboard!=null)floating.PreviewKeyDown+=(s,e)=>keyboard(e);if(drop!=null){floating.AllowDrop=true;floating.PreviewDragOver+=(s,e)=>{e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};floating.PreviewDrop+=(s,e)=>{if(e.Data.GetDataPresent(DataFormats.FileDrop))drop((string[])e.Data.GetData(DataFormats.FileDrop));e.Handled=true;};}floating.Show();
  }
  void FloatingClosing(object sender,System.ComponentModel.CancelEventArgs e) {if(ownerClosing||floating==null)return;e.Cancel=true;if(busy||restoreScheduled)return;restoreScheduled=true;owner.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(async()=> {try{if(floating!=null)await Toggle();}finally{restoreScheduled=false;}}));}
  void RestoreImmediately() {var old=floating;if(old==null)return;floating=null;old.Closing-=FloatingClosing;old.Content=null;if(original!=null){original.Children.Remove(placeholder);if(!original.Children.Contains(pane))original.Children.Insert(Math.Min(index,original.Children.Count),pane);}old.Close();placeholder=null;UpdateButton();}
 }
}
