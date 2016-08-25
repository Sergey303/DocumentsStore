using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Archive;
using PolarDB;

namespace GetImageFromDZPxCell
{
    class Program
    {
        private static PxCell cell;
        private static void Write(PxCell xcell, FileStream fs)
        {
            xcell.Root.Set(new object[] { 99, new object[0] });
            xcell.Root.Field(1).SetRepeat(fs.Length);
            PxEntry zel = xcell.Root.Field(1).Element(0);
            fs.Position = 0L;
            xcell.BasicStream.Position = zel.offset;
            fs.CopyTo(xcell.BasicStream);
            xcell.BasicStream.Flush();
        }
        static void Main(string[] args)
        {
            Create("../../0174.sarc2");
            GetImageOfSize(new Size(3000, 2000));

        }

        public static void GetImageOfSize(Size needSize)
        {
            Size maxSize = new Size((int) cell.Root.Field(0).Get(), (int) cell.Root.Field(1).Get());
            int level = (int) cell.Root.Field(2).Count()-1;
            int findedWidth;
            var levelw = GetMaxLevel(needSize.Width, maxSize.Width, level, out findedWidth);
            int findedHeight;
            var levelh= GetMaxLevel(needSize.Height, maxSize.Height, level, out findedHeight);

            
            float changeWidth = 1f, changeHeight = 1f;
            float aspectRatio = maxSize.Width * 1f / maxSize.Height;
            if (levelh > levelw)
            {
                level = levelh;
                changeHeight = needSize.Height*1f/findedHeight;
                changeWidth = needSize.Width*1f /(findedHeight * aspectRatio);
            }
            else
            {
                level = levelw;
                changeHeight = needSize.Height * aspectRatio / findedWidth;
                changeWidth = needSize.Width * 1f / findedWidth;
            }
            Bitmap outputImage = new Bitmap(needSize.Width, needSize.Height);
            using (Graphics graphics = Graphics.FromImage(outputImage))
            {
                //      graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (var wrapMode = new ImageAttributes())
                {
                    wrapMode.SetWrapMode(WrapMode.TileFlipXY);

                    int x = 0;
                    var xc = cell.Root.Field(2).Element(level).Count();
                    for (int i = 0; i < xc; i++) //x
                    {
                        var yc = cell.Root.Field(2).Element(level).Element(i).Count();
                        int y = 0;
                        int w = 0;
                        for (int j = 0; j < yc; j++) //y
                        {
                            var img = GetImage(level, i, j);
                            w = (int) (img.Size.Width*changeWidth);
                           var h = (int) (img.Height*changeHeight);
                            graphics.DrawImage(img, new Rectangle(new Point(x, y), new Size(w, h)),
                                0, 0, img.Size.Width, img.Size.Height, GraphicsUnit.Pixel, wrapMode);
                            y += h;
                        }
                        x += w;
                    }
                }
            }
            outputImage.Save("../../0174.jpg");
        }

        private static int GetMaxLevel(int needwidth, int currnetWidth, int level, out int finded)
        {
            needwidth += needwidth;
            while (needwidth <= currnetWidth) // need <= currnet/2
            {
                level--;
                currnetWidth = currnetWidth/2;
            }
            finded = currnetWidth;
            return level;
        }

        static Image GetImage(int level, int x, int y)
        {


            return new Bitmap(new MemoryStream(((object[])cell.Root.Field(2).Element(level).Element(x).Element(y).Get())
                .Cast<byte>().ToArray()));

        }

        private static void Create(string sarc2path)
        {
           
            Stream stream = new FileStream(sarc2path, FileMode.Open, FileAccess.Read);
            
            long offset = 0L;
            for (int i = 0; i < 16; i++)
            {
                int b = stream.ReadByte();
                if (b != 0) offset = offset*10 + (b - '0');
            }

            byte[] catalog_bytes = new byte[offset - 16];

            stream.Read(catalog_bytes, 0, (int) (offset - 16));
            int width =0;
            int height=0;

            var levels = XElement.Load(new XmlTextReader(new MemoryStream(catalog_bytes))).Elements("file")
                .Select(xfile =>
                {
                    string r_path = xfile.Element("path").Value;
                    var regex = new Regex(@"(?<level>[0-9]+)/(?<x>[0-9]+)_(?<y>[0-9]+)\.jpg$");

                    var match = regex.Match(r_path);
                    if (!match.Success)
                        if (Path.GetExtension(r_path) == ".xml")
                        {
                            stream.Seek(long.Parse(xfile.Element("start").Value) + offset, SeekOrigin.Begin);
                            var dataxml = new byte[long.Parse(xfile.Element("length").Value)];
                            stream.Read(dataxml, 0, dataxml.Length);
                            XElement xDzi = XElement.Load(XmlReader.Create(new MemoryStream(dataxml)));
                            var xElement =
                                xDzi.Element(XNamespace.Get("http://schemas.microsoft.com/deepzoom/2009") + "Size");

                            width = int.Parse(xElement.Attribute("Width").Value);
                            height = int.Parse(xElement.Attribute("Height").Value);

                            return null;
                        }
                        else throw new Exception();
                    int level = Convert.ToInt32(match.Groups["level"].Value);
                    int x = Convert.ToInt32(match.Groups["x"].Value);
                    int y = Convert.ToInt32(match.Groups["y"].Value);
                    stream.Seek(long.Parse(xfile.Element("start").Value) + offset, SeekOrigin.Begin);
                    var data = new byte[long.Parse(xfile.Element("length").Value)];
                    stream.Read(data, 0, data.Length);
                    return new {level, x, y, data = data.Cast<object>().ToArray()};
                })
                .Where(arg => arg != null)
                .GroupBy(arg => arg.level)
                .OrderBy(level => level.Key)
                .Select(gl =>
                    gl.GroupBy(arg => arg.x)
                        .OrderBy(xs => xs.Key)
                        .Select(gx =>
                            gx.OrderBy(arg => arg.y)
                                .Select(arg => arg.data)
                                .ToArray())
                        .ToArray())
                .ToArray();
            stream.Close();

            if (width==0 || height==0) throw new Exception();

            cell =
               new PxCell(
                   new PTypeRecord(new NamedType("width", new PType(PTypeEnumeration.integer)),
                       new NamedType("height", new PType(PTypeEnumeration.integer)),
                       new NamedType("images",
                           new PTypeSequence( //by level
                               new PTypeSequence( //by x
                                   new PTypeSequence( //by y
                                       new PTypeSequence(new PType(PTypeEnumeration.@byte))))))),
                   Path.ChangeExtension(sarc2path, ".dz_px"), false);


            cell.Fill(new object[] { width, height, levels });
        }
    }
}
