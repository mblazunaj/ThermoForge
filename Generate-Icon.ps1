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
  static Color ColorAt(int rgb) { return Color.FromArgb(unchecked((int)0xff000000) | rgb); }

  static void FillRounded(Graphics g, Brush brush, int inset, int radius) {
    int width = 256 - 2 * inset;
    using (var path = Rounded(inset, inset, width, width, radius))
      g.FillPath(brush, path);
  }

  static LinearGradientBrush Gradient(int top, int bottom, int start, int end) {
    return new LinearGradientBrush(new Point(0, top), new Point(0, bottom), ColorAt(start), ColorAt(end));
  }

  // Geometry matches ThermoForge.svg on a 256 by 256 grid.
  static Bitmap Render(int size) {
    var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bitmap)) {
      g.SmoothingMode = SmoothingMode.AntiAlias;
      g.PixelOffsetMode = PixelOffsetMode.HighQuality;
      g.ScaleTransform(size / 256f, size / 256f);
      using (var rim = Gradient(8, 248, 0x56647c, 0x313a4b))
        FillRounded(g, rim, 8, 56);
      using (var surface = Gradient(12, 244, 0x303949, 0x141a25))
        FillRounded(g, surface, 12, 52);

      using (var contacts = new Pen(ColorAt(0xa8b8cf), 14)) {
        contacts.StartCap = contacts.EndCap = LineCap.Round;
        foreach (int position in new[] { 88, 168 }) {
          g.DrawLine(contacts, position, 35, position, 55);
          g.DrawLine(contacts, position, 201, position, 221);
          g.DrawLine(contacts, 35, position, 55, position);
          g.DrawLine(contacts, 201, position, 221, position);
        }
      }
      using (var frame = new SolidBrush(ColorAt(0x8494ad)))
        FillRounded(g, frame, 48, 31);
      using (var center = new SolidBrush(ColorAt(0x111824)))
        FillRounded(g, center, 55, 24);

      using (var heat = Gradient(75, 183, 0xffcd76, 0xff794d))
      using (var thermometer = new GraphicsPath()) {
        thermometer.AddBezier(128, 75, 136, 75, 142, 81, 142, 89);
        thermometer.AddLine(142, 89, 142, 121);
        thermometer.AddBezier(142, 121, 142, 127, 145, 130, 149, 134);
        thermometer.AddBezier(149, 134, 155, 140, 158, 146, 158, 153);
        thermometer.AddBezier(158, 153, 158, 170, 145, 183, 128, 183);
        thermometer.AddBezier(128, 183, 111, 183, 98, 170, 98, 153);
        thermometer.AddBezier(98, 153, 98, 146, 101, 140, 107, 134);
        thermometer.AddBezier(107, 134, 111, 130, 114, 127, 114, 121);
        thermometer.AddLine(114, 121, 114, 89);
        thermometer.AddBezier(114, 89, 114, 81, 120, 75, 128, 75);
        thermometer.CloseFigure();
        g.FillPath(heat, thermometer);
      }
    }
    return bitmap;
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
