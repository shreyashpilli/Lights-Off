$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$builderSource = @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;

public static class LightsOffIconBuilder
{
    private static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
    {
        GraphicsPath p = new GraphicsPath();
        p.AddArc(x, y, r * 2, r * 2, 180, 90);
        p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
        p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static byte[] RenderFrame(int size, bool awake)
    {
        using (Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bitmap))
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.ScaleTransform(size / 64F, size / 64F);
            Color screen = Color.FromArgb(15, 17, 23);
            Color violet = Color.FromArgb(129, 132, 255);
            using (GraphicsPath outer = RoundRect(8, 10, 48, 38, 7))
            using (SolidBrush shell = new SolidBrush(Color.FromArgb(30, 32, 40)))
            using (Pen outline = new Pen(violet, 3.5F))
            {
                g.FillPath(shell, outer);
                g.DrawPath(outline, outer);
            }
            using (GraphicsPath inner = RoundRect(12, 14, 40, 29, 3))
            using (SolidBrush display = new SolidBrush(screen))
                g.FillPath(display, inner);
            using (Pen stand = new Pen(violet, 3.5F))
            {
                stand.StartCap = LineCap.Round;
                stand.EndCap = LineCap.Round;
                g.DrawLine(stand, 30, 48, 30, 53);
                g.DrawLine(stand, 22, 56, 42, 56);
            }
            using (SolidBrush moon = new SolidBrush(Color.FromArgb(165, 168, 255)))
            using (SolidBrush cut = new SolidBrush(screen))
            using (Pen star = new Pen(violet, 1.7F))
            {
                g.FillEllipse(moon, 32, 19, 16, 16);
                g.FillEllipse(cut, 39, 16, 16, 16);
                star.StartCap = LineCap.Round;
                star.EndCap = LineCap.Round;
                g.DrawLine(star, 20, 21, 23, 21);
                g.DrawLine(star, 21.5F, 19.5F, 21.5F, 22.5F);
            }
            if (awake)
            {
                using (SolidBrush badge = new SolidBrush(Color.FromArgb(34, 197, 94)))
                using (Pen edge = new Pen(screen, 2.5F))
                using (SolidBrush bolt = new SolidBrush(Color.FromArgb(12, 46, 25)))
                using (GraphicsPath lightning = new GraphicsPath())
                {
                    g.FillEllipse(badge, 41, 37, 18, 18);
                    g.DrawEllipse(edge, 41, 37, 18, 18);
                    lightning.AddPolygon(new PointF[] {
                        new PointF(51, 39), new PointF(46, 47), new PointF(50, 47),
                        new PointF(48, 53), new PointF(55, 44), new PointF(51, 44)
                    });
                    g.FillPath(bolt, lightning);
                }
            }
            int maskStride = ((size + 31) / 32) * 4;
            writer.Write((uint)40);
            writer.Write(size);
            writer.Write(size * 2);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)0);
            writer.Write((uint)(size * size * 4));
            writer.Write(0);
            writer.Write(0);
            writer.Write((uint)0);
            writer.Write((uint)0);
            for (int y = size - 1; y >= 0; y--)
                for (int x = 0; x < size; x++)
                {
                    Color pixel = bitmap.GetPixel(x, y);
                    writer.Write(pixel.B);
                    writer.Write(pixel.G);
                    writer.Write(pixel.R);
                    writer.Write(pixel.A);
                }
            byte[] mask = new byte[maskStride * size];
            for (int y = size - 1; y >= 0; y--)
                for (int x = 0; x < size; x++)
                    if (bitmap.GetPixel(x, y).A < 128)
                        mask[(size - 1 - y) * maskStride + (x / 8)] |= (byte)(0x80 >> (x % 8));
            writer.Write(mask);
            writer.Flush();
            return stream.ToArray();
        }
    }

    public static void WriteIcon(string path, bool awake)
    {
        int[] sizes = new int[] { 16, 24, 32, 48, 64, 256 };
        List<byte[]> frames = new List<byte[]>();
        foreach (int size in sizes) frames.Add(RenderFrame(size, awake));
        using (FileStream file = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter writer = new BinaryWriter(file))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)sizes.Length);
            int offset = 6 + sizes.Length * 16;
            for (int i = 0; i < sizes.Length; i++)
            {
                int size = sizes[i];
                writer.Write((byte)(size == 256 ? 0 : size));
                writer.Write((byte)(size == 256 ? 0 : size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write((uint)frames[i].Length);
                writer.Write((uint)offset);
                offset += frames[i].Length;
            }
            foreach (byte[] frame in frames) writer.Write(frame);
        }
    }
}
'@

Add-Type -TypeDefinition $builderSource -ReferencedAssemblies 'System.Drawing.dll'
$root = Split-Path -Parent $PSScriptRoot
$assetDir = Join-Path $root 'assets'
New-Item -ItemType Directory -Force -Path $assetDir | Out-Null
[LightsOffIconBuilder]::WriteIcon((Join-Path $assetDir 'LightsOff-Normal.ico'), $false)
[LightsOffIconBuilder]::WriteIcon((Join-Path $assetDir 'LightsOff-Awake.ico'), $true)
Write-Output "Created normal-sleep and keep-awake tray icons in $assetDir"
