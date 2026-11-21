using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace FrameScout {
 public static class ApiDialog {
  public static void Show(Window owner,ApiConfig target) {
   var window=new Window {Owner=owner,Title="API 与模型设置",Width=610,Height=Math.Min(700,SystemParameters.WorkArea.Height*0.92),WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=owner.Background,Foreground=owner.Foreground,FontFamily=owner.FontFamily};window.Resources.MergedDictionaries.Add(owner.Resources);
   var stack=new StackPanel {Margin=new Thickness(20)};window.Content=new ScrollViewer {Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
   Action<string> label=s=>stack.Children.Add(new TextBlock {Text=s,Margin=new Thickness(0,8,0,6),TextWrapping=TextWrapping.Wrap});
   label("使用支持 OpenAI 兼容格式的文本接口。填写地址和密钥后会自动读取模型列表，也可以手动填写模型名称。");
   label("接口地址（根地址 /v1 或完整 chat/completions 地址）");var endpoint=new TextBox {Text=target.Endpoint};stack.Children.Add(endpoint);
   label("API 密钥");var key=new PasswordBox {Password=target.Key,Padding=new Thickness(8),Background=(Brush)new BrushConverter().ConvertFromString("#111823"),Foreground=Brushes.White,BorderBrush=(Brush)new BrushConverter().ConvertFromString("#40516A")};stack.Children.Add(key);
   var remember=new CheckBox {Content="在本机加密保存密钥（仅当前 Windows 用户可解密）",IsChecked=target.Remember,Margin=new Thickness(0,10,0,3)};stack.Children.Add(remember);
   label("模型名称（可手动填写）");var model=new TextBox {Text=target.Model};stack.Children.Add(model);
   var models=new ListBox {Height=140,Background=(Brush)new BrushConverter().ConvertFromString("#111823"),Foreground=Brushes.White,BorderBrush=(Brush)new BrushConverter().ConvertFromString("#40516A"),Margin=new Thickness(0,8,0,0)};stack.Children.Add(models);models.SelectionChanged+=(s,e)=> {if(models.SelectedItem!=null)model.Text=models.SelectedItem.ToString();};
   var status=new TextBlock {Text="模型列表代表接口返回的授权模型；测试连接会验证所选模型能否回答文本。",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)new BrushConverter().ConvertFromString("#8FA2BD"),Margin=new Thickness(0,8,0,3)};stack.Children.Add(status);
   label("每次请求超时（10 至 600 秒）");var timeout=new TextBox {Text=target.TimeoutSeconds.ToString(),Width=90,HorizontalAlignment=HorizontalAlignment.Left};stack.Children.Add(timeout);
   var buttons=new WrapPanel {Margin=new Thickness(0,14,0,0)};var read=new Button {Content="重新读取模型"};var test=new Button {Content="测试连接"};var save=new Button {Content="保存设置"};buttons.Children.Add(read);buttons.Children.Add(test);buttons.Children.Add(save);stack.Children.Add(buttons);
   var note=new TextBlock {Text="校对时只发送剧本及字幕文本，视频不会上传。密钥不会写入导出报告。未勾选保存时仅保留到本次程序退出。",TextWrapping=TextWrapping.Wrap,Foreground=(Brush)new BrushConverter().ConvertFromString("#8FA2BD"),Margin=new Thickness(0,12,0,0)};stack.Children.Add(note);
   CancellationTokenSource request=null;bool closed=false;var debounce=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(1100)};
   Func<ApiConfig> values=()=> {int seconds;if(!int.TryParse(timeout.Text,out seconds)||seconds<10||seconds>600)throw new ArgumentException("请求超时请填 10 至 600 秒。");var c=new ApiConfig {Endpoint=endpoint.Text.Trim(),Key=key.Password.Trim(),Model=model.Text.Trim(),Remember=remember.IsChecked==true,TimeoutSeconds=seconds};c.BaseUrl();return c;};
   Func<bool,Task> fetch=async verify=> {if(request!=null)request.Cancel();request=new CancellationTokenSource();var ct=request.Token;try {var c=values();if(c.Key.Length==0&&!(new Uri(c.BaseUrl()).IsLoopback)) {status.Text="请先填写 API 密钥。";return;}status.Text=verify?"正在测试所选文本模型…":"正在读取模型列表…";
    if(verify) {await new AiClient(c).Json("这是连接测试。请返回 {\"ok\":true}。","测试连接",ct);if(!ct.IsCancellationRequested&&!closed)status.Text="连接成功，所选模型可返回文本 JSON。";}
    else {var list=await new AiClient(c).Models(ct);if(ct.IsCancellationRequested||closed)return;models.ItemsSource=list;if(list.Contains(model.Text))models.SelectedItem=model.Text;else if(model.Text.Trim().Length==0) {var first=list.FirstOrDefault(x=>!new[]{"embedding","image","audio","tts","whisper","rerank","vision-only"}.Any(t=>x.ToLowerInvariant().Contains(t)));if(first!=null){models.SelectedItem=first;model.Text=first;}}status.Text="已读取 "+list.Count+" 个模型。可选择后点击“测试连接”。";}
   }catch(OperationCanceledException) {if(!ct.IsCancellationRequested&&!closed)status.Text="请求超时，请检查地址或增加超时。";}catch(Exception ex) {if(!ct.IsCancellationRequested&&!closed)status.Text=ex.Message;}};
   debounce.Tick+=async(s,e)=> {debounce.Stop();await fetch(false);};Action changed=()=> {debounce.Stop();if(key.Password.Length>0||endpoint.Text.Contains("localhost")||endpoint.Text.Contains("127.0.0.1"))debounce.Start();};endpoint.TextChanged+=(s,e)=>changed();key.PasswordChanged+=(s,e)=>changed();read.Click+=async(s,e)=> {debounce.Stop();await fetch(false);};test.Click+=async(s,e)=> {debounce.Stop();await fetch(true);};
   save.Click+=(s,e)=> {try {var c=values();target.Endpoint=c.Endpoint;target.Key=c.Key;target.Model=c.Model;target.Remember=c.Remember;target.TimeoutSeconds=c.TimeoutSeconds;target.Save();window.Close();}catch(Exception ex) {status.Text=ex.Message;}};
   window.Closing+=(s,e)=> {closed=true;debounce.Stop();if(request!=null)request.Cancel();};window.Loaded+=(s,e)=>changed();window.ShowDialog();
  }
 }
}
