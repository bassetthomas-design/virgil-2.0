# Dessin validé le 5/11. Aucun paquet, service ou moteur de génération d'images.
# Le petit rasteriseur ci-dessous lit uniquement le sous-ensemble SVG de cette source :
# cercles, rectangles arrondis, deux dégradés et contour. Échantillonnage 4 x 4, alpha droit.
param(
    [string]$SourcePath = (Join-Path $PSScriptRoot '../assets/virgil-visage.svg'),
    [string]$OutputPath = (Join-Path $PSScriptRoot '../assets/virgil.ico')
)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Xml;
using System.Globalization;
using System.Collections.Generic;

public static class VirgilIconWriter
{
    static double Number(XmlElement e, string name, double fallback = 0)
    {
        string value = e.GetAttribute(name);
        return value.Length == 0 ? fallback : double.Parse(value, CultureInfo.InvariantCulture);
    }
    static double[] Color(string value)
    {
        return new double[] { Convert.ToByte(value.Substring(1, 2), 16),
            Convert.ToByte(value.Substring(3, 2), 16), Convert.ToByte(value.Substring(5, 2), 16) };
    }
    static double[] Mix(double[] first, double[] last, double amount)
    {
        amount = Math.Max(0, Math.Min(1, amount));
        return new double[] { first[0] + (last[0] - first[0]) * amount,
            first[1] + (last[1] - first[1]) * amount, first[2] + (last[2] - first[2]) * amount };
    }
    static byte[] Bitmap(XmlDocument svg, int size)
    {
        var ns = new XmlNamespaceManager(svg.NameTable); ns.AddNamespace("s", "http://www.w3.org/2000/svg");
        var nodes = svg.SelectNodes("/s:svg/s:circle | /s:svg/s:rect", ns);
        var rim = svg.SelectNodes("//s:linearGradient[@id='rim']/s:stop", ns);
        var face = svg.SelectNodes("//s:radialGradient[@id='face']/s:stop", ns);
        var rimFirst = Color(((XmlElement)rim[0]).GetAttribute("stop-color"));
        var rimLast = Color(((XmlElement)rim[1]).GetAttribute("stop-color"));
        var faceFirst = Color(((XmlElement)face[0]).GetAttribute("stop-color"));
        var faceLast = Color(((XmlElement)face[1]).GetAttribute("stop-color"));
        var pixels = new byte[size * size * 4];
        const int samples = 4;
        double extent = Number(svg.DocumentElement, "width");
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            double red = 0, green = 0, blue = 0, alpha = 0;
            for (int sy = 0; sy < samples; sy++)
            for (int sx = 0; sx < samples; sx++)
            {
                double px = (x + (sx + 0.5) / samples) * extent / size;
                double py = (y + (sy + 0.5) / samples) * extent / size;
                double[] color = null;
                foreach (XmlElement node in nodes)
                {
                    string fill = node.GetAttribute("fill");
                    if (node.LocalName == "circle")
                    {
                        double cx = Number(node, "cx"), cy = Number(node, "cy"), radius = Number(node, "r");
                        double distance = Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                        if (fill == "none")
                        {
                            if (color != null && Math.Abs(distance - radius) <= Number(node, "stroke-width") / 2)
                                color = Mix(color, Color(node.GetAttribute("stroke")), Number(node, "stroke-opacity", 1));
                        }
                        else if (distance <= radius)
                        {
                            if (fill == "url(#rim)") color = Mix(rimFirst, rimLast, (py - cy + radius) / (radius * 2));
                            else if (fill == "url(#face)")
                            {
                                double dx = (px - cx) / (radius * 2), dy = (py - cy) / (radius * 2) + 0.1;
                                color = Mix(faceFirst, faceLast, Math.Sqrt(dx * dx + dy * dy) / 0.6);
                            }
                            else throw new InvalidDataException("Remplissage SVG inconnu.");
                        }
                    }
                    else
                    {
                        double left = Number(node, "x"), top = Number(node, "y");
                        double width = Number(node, "width"), height = Number(node, "height"), rx = Number(node, "rx");
                        double dx = Math.Max(Math.Abs(px - left - width / 2) - (width / 2 - rx), 0);
                        double dy = Math.Max(Math.Abs(py - top - height / 2) - (height / 2 - rx), 0);
                        if (dx * dx + dy * dy <= rx * rx) color = Color(fill);
                    }
                }
                if (color != null) { red += color[0]; green += color[1]; blue += color[2]; alpha++; }
            }
            int offset = (y * size + x) * 4;
            if (alpha > 0)
            {
                pixels[offset] = (byte)Math.Round(blue / alpha); pixels[offset + 1] = (byte)Math.Round(green / alpha);
                pixels[offset + 2] = (byte)Math.Round(red / alpha); pixels[offset + 3] = (byte)Math.Round(alpha * 255 / (samples * samples));
            }
        }
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            int maskStride = ((size + 31) / 32) * 4;
            writer.Write(40); writer.Write(size); writer.Write(size * 2); writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(0); writer.Write(size * size * 4 + maskStride * size);
            writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            for (int y = size - 1; y >= 0; y--) writer.Write(pixels, y * size * 4, size * 4);
            for (int y = size - 1; y >= 0; y--)
            {
                var mask = new byte[maskStride];
                for (int x = 0; x < size; x++) if (pixels[(y * size + x) * 4 + 3] == 0) mask[x / 8] |= (byte)(128 >> (x % 8));
                writer.Write(mask);
            }
            return stream.ToArray();
        }
    }
    public static void Build(string source, string destination)
    {
        var svg = new XmlDocument { XmlResolver = null }; svg.Load(source);
        int[] sizes = { 16, 32, 48, 64, 128, 256 };
        var frames = new List<byte[]>(); foreach (int size in sizes) frames.Add(Bitmap(svg, size));
        using (var writer = new BinaryWriter(File.Create(destination)))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
            }
            foreach (var frame in frames) writer.Write(frame);
        }
    }
}
'@
[VirgilIconWriter]::Build([IO.Path]::GetFullPath($SourcePath), [IO.Path]::GetFullPath($OutputPath))
Write-Output "Icône créée : $OutputPath (16, 32, 48, 64, 128 et 256 px)."
