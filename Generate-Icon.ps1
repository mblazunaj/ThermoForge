param([string]$Destination = "$PSScriptRoot\ThermoForge\Assets")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing.Common
Add-Type -ReferencedAssemblies @('System.Drawing.Common', 'System.Drawing.Primitives', 'System.Private.Windows.GdiPlus', 'System.Private.Windows.Core') -TypeDefinition @'
using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
public static class ThermoForgeIconBuilder
{
  static GraphicsPath Rounded(float x,float y,float w,float h,float r) {
    var p=new GraphicsPath();float d=r*2;
    p.AddArc(x,y,d,d,180,90);p.AddArc(x+w-d,y,d,d,270,90);p.AddArc(x+w-d,y+h-d,d,d,0,90);p.AddArc(x,y+h-d,d,d,90,90);p.CloseFigure();return p;
  }
  static Bitmap Render(int size) {
    var b=new Bitmap(size,size,PixelFormat.Format32bppArgb);
    using(var g=Graphics.FromImage(b)) {
      g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.Clear(Color.Transparent);g.ScaleTransform(size/256f,size/256f);
      using(var bg=new SolidBrush(Color.FromArgb(23,23,25))) using(var p=Rounded(8,8,240,240,52))g.FillPath(bg,p);
      using(var pen=new Pen(Color.FromArgb(216,218,221),8)){pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
        foreach(int v in new[]{88,128,168}){g.DrawLine(pen,v,38,v,58);g.DrawLine(pen,v,198,v,218);g.DrawLine(pen,38,v,58,v);g.DrawLine(pen,198,v,218,v);}
        using(var p=Rounded(58,58,140,140,20))using(var fill=new SolidBrush(Color.FromArgb(34,34,37))){g.FillPath(fill,p);g.DrawPath(pen,p);}
      }
      using(var p=new GraphicsPath()) {
        p.StartFigure();p.AddLine(112,144,112,96);p.AddBezier(112,96,112,75,144,75,144,96);p.AddLine(144,96,144,144);p.AddBezier(144,144,156,153,158,166,152,177);p.AddBezier(152,177,146,191,126,196,114,185);p.AddBezier(114,185,100,174,100,156,112,144);p.CloseFigure();
        using(var brush=new SolidBrush(Color.FromArgb(241,242,244)))g.FillPath(brush,p);
      }
      using(var orange=new SolidBrush(Color.FromArgb(255,135,72))) {
        using(var p=Rounded(122,98,12,67,6))g.FillPath(orange,p);g.FillEllipse(orange,113,151,30,30);
      }
    }
    return b;
  }
  public static void Write(string folder) {
    Directory.CreateDirectory(folder);var sizes=new[]{16,24,32,48,64,96,128,256};var images=new byte[sizes.Length][];
    for(int i=0;i<sizes.Length;i++) using(var b=Render(sizes[i]))using(var s=new MemoryStream()){b.Save(s,ImageFormat.Png);images[i]=s.ToArray();}
    using(var w=new BinaryWriter(File.Create(Path.Combine(folder,"ThermoForge.ico")))){
      w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)sizes.Length);uint offset=(uint)(6+16*sizes.Length);
      for(int i=0;i<sizes.Length;i++){w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)0);w.Write((byte)0);w.Write((ushort)1);w.Write((ushort)32);w.Write((uint)images[i].Length);w.Write(offset);offset+=(uint)images[i].Length;}
      foreach(var bytes in images)w.Write(bytes);
    }
    using(var b=Render(512))b.Save(Path.Combine(folder,"ThermoForge.png"),ImageFormat.Png);
  }
}
'@
[ThermoForgeIconBuilder]::Write($Destination)
