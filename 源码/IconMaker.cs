using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Collections.Generic;
class IconMaker {
 static GraphicsPath Round(RectangleF r,float radius) { var p=new GraphicsPath();float d=radius*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p; }
 static Bitmap Draw(int size) {
  var bmp=new Bitmap(size,size);using(var g=Graphics.FromImage(bmp)) {g.SmoothingMode=SmoothingMode.AntiAlias;g.ScaleTransform(size/256f,size/256f);
   using(var bg=new LinearGradientBrush(new Point(20,5),new Point(230,250),Color.FromArgb(34,57,91),Color.FromArgb(12,26,45)))using(var shape=Round(new RectangleF(6,6,244,244),55))g.FillPath(bg,shape);
   using(var frame=Round(new RectangleF(42,49,153,153),19))using(var brush=new LinearGradientBrush(new Point(40,40),new Point(180,200),Color.FromArgb(104,244,211),Color.FromArgb(43,160,200)))g.FillPath(brush,frame);
   using(var hole=Round(new RectangleF(64,70,109,109),8))using(var b=new SolidBrush(Color.FromArgb(20,40,60)))g.FillPath(b,hole);
   using(var p=new Pen(Color.FromArgb(21,50,66),6))for(int y=65;y<190;y+=27) {g.DrawLine(p,48,y,57,y);g.DrawLine(p,179,y,188,y);}
   using(var p=new Pen(Color.FromArgb(111,241,213),7)) {p.LineJoin=LineJoin.Round;g.DrawLines(p,new[]{new Point(77,128),new Point(96,128),new Point(106,108),new Point(121,148),new Point(134,123),new Point(155,123)});}
   using(var b=new SolidBrush(Color.FromArgb(17,31,51)))g.FillEllipse(b,133,131,82,82);
   using(var p=new Pen(Color.FromArgb(240,250,255),12)) {g.DrawEllipse(p,143,140,58,58);p.StartCap=LineCap.Round;p.EndCap=LineCap.Round;g.DrawLine(p,195,194,222,221);}
   using(var p=new Pen(Color.FromArgb(101,238,206),7)) {p.StartCap=LineCap.Round;p.EndCap=LineCap.Round;g.DrawLines(p,new[]{new Point(158,168),new Point(168,177),new Point(184,158)});}
  }return bmp;
 }
 static int Main(string[] args) {Directory.CreateDirectory(args[0]);var images=new List<byte[]>();int[] sizes={16,24,32,48,64,128,256};foreach(int s in sizes)using(var b=Draw(s))using(var ms=new MemoryStream()) {b.Save(ms,ImageFormat.Png);images.Add(ms.ToArray());if(s==256)b.Save(Path.Combine(args[0],"视频巡检.png"),ImageFormat.Png);}
  using(var file=File.Create(Path.Combine(args[0],"视频巡检.ico")))using(var w=new BinaryWriter(file)) {w.Write((short)0);w.Write((short)1);w.Write((short)sizes.Length);int offset=6+16*sizes.Length;for(int i=0;i<sizes.Length;i++) {w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)0);w.Write((byte)0);w.Write((short)1);w.Write((short)32);w.Write(images[i].Length);w.Write(offset);offset+=images[i].Length;}foreach(var image in images)w.Write(image);}return 0; }
}
