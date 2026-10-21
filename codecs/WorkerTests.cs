using System;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ImageMagick;
internal static class WorkerTests {
    static int checks;
    static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
    static byte[] Hash(string path) { using(var stream=File.OpenRead(path)) using(var sha=SHA256.Create()) return sha.ComputeHash(stream); }
    static string worker, output;
    static int Main(string[] args) {
        try {
            worker=args[0]; output=args[1]; Directory.CreateDirectory(output);
            // Fixture generation has the library's default configuration; the separate decoder
            // process explicitly loads its restrictive bundled policy.
            MagickNET.Initialize();
            foreach(var f in MagickNET.SupportedFormats) if (f.Format==MagickFormat.Heic || f.Format==MagickFormat.Dng || f.Format==MagickFormat.Xcf) Console.WriteLine("CAPABILITY " + f.Format + " read=" + f.SupportsReading + " write=" + f.SupportsWriting + " " + f.Version);
            using(var image=new MagickImage(MagickColors.CornflowerBlue,128,96)) {
                string[] formats={"Png","Jpeg","Bmp","Gif","Tiff","WebP","Avif","Jxl","Ico","Tga","Pcx","Ppm","Jp2","Dds","Hdr","Exr","Qoi","Psd","Psb"};
                string[] exts={"png","jpg","bmp","gif","tif","webp","avif","jxl","ico","tga","pcx","ppm","jp2","dds","hdr","exr","qoi","psd","psb"};
                for(int i=0;i<formats.Length;i++) {
                    var format=(MagickFormat)Enum.Parse(typeof(MagickFormat),formats[i],true);
                    string path=Path.Combine(output,"sample."+exts[i]);
                    File.WriteAllBytes(path,image.ToByteArray(format));
                    Run(path,true,0,1);
                    if(formats[i]=="Psd" || formats[i]=="Psb") Run(path,true,0,1,0);
                }
                foreach(string ext in new[]{"ora","kra"}) {
                    string path=Path.Combine(output,"sample."+ext);
                    using(var zip=ZipFile.Open(path,ZipArchiveMode.Create)) using(var entry=zip.CreateEntry("mergedimage.png").Open()) { byte[] data=image.ToByteArray(MagickFormat.Png);entry.Write(data,0,data.Length); }
                    Run(path,true,0,1);
                }
                using(var collection=new MagickImageCollection()) {
                    collection.Add(image.Clone()); collection.Add(new MagickImage(MagickColors.Red,128,96));
                    foreach(string ext in new[]{"gif","tiff"}) { string path=Path.Combine(output,"pages."+ext); File.WriteAllBytes(path,collection.ToByteArray(ext=="gif"?MagickFormat.Gif:MagickFormat.Tiff));Run(path,true,1,2);Run(path,false,2,0); }
                }
            }
            string svg=Path.Combine(output,"simple.svg");
            File.WriteAllText(svg,"<svg xmlns='http://www.w3.org/2000/svg' width='128' height='96'><rect width='128' height='96' fill='red'/></svg>");Run(svg,true,0,1);
            string svgz=Path.Combine(output,"simple.svgz");using(var zipped=File.Create(svgz))using(var gz=new GZipStream(zipped,CompressionMode.Compress))using(var original=File.OpenRead(svg))original.CopyTo(gz);Run(svgz,true,0,1);
            string xcf=Path.Combine(output,"layers.xcf");WriteXcf(xcf);
            Run(xcf,true,0,1);Run(xcf,false,1,0);
            foreach(var item in new[]{
                new[]{"external.svg","<svg xmlns='http://www.w3.org/2000/svg'><image href='file:///C:/Windows/win.ini'/></svg>"},
                new[]{"entity.svg","<!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]><svg xmlns='http://www.w3.org/2000/svg'>&x;</svg>"},
                new[]{"script.svg","<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"},
                new[]{"url.svg","<svg xmlns='http://www.w3.org/2000/svg'><rect fill='url(https://example.com/a)'/></svg>"},
                new[]{"malformed.psd","8BPS rubbish"},new[]{"malformed.xcf","gimp xcf rubbish"},new[]{"malformed.avif","garbage"},
                new[]{"renamed.png","push graphic-context\nviewbox 0 0 10 10\nimage over 0,0 0,0 'https://example.com/x'\npop graphic-context"},
                new[]{"unsupported.mvg","push graphic-context"}
            }) { string path=Path.Combine(output,item[0]);File.WriteAllText(path,item[1]); Run(path,false,0,0); }
            string empty=Path.Combine(output,"no-preview.kra");using(var zip=ZipFile.Open(empty,ZipArchiveMode.Create)) zip.CreateEntry("preview.png");Run(empty,false,0,0);
            string duplicate=Path.Combine(output,"duplicate.ora");using(var zip=ZipFile.Open(duplicate,ZipArchiveMode.Create)) {zip.CreateEntry("mergedimage.png");zip.CreateEntry("mergedimage.png");}Run(duplicate,false,0,0);
            // Sparse oversized input exercises bounds without retaining a large fixture.
            string huge=Path.Combine(output,"huge.png"); using(var file=File.Create(huge)) file.SetLength(512L*1024*1024+1);Run(huge,false,0,0);File.Delete(huge);
            Console.WriteLine(checks+" codec checks passed.");return 0;
        } catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    static void Run(string path,bool success,int frame,int count,int edge=64) {
        byte[] before=Hash(path);
        var start=new ProcessStartInfo(worker,"\""+path+"\" "+edge+" "+frame) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        using(var process=Process.Start(start)) using(var data=new MemoryStream()) {
            // Output for these fixtures is only a few hundred bytes, below pipe capacity.
            process.StandardOutput.BaseStream.CopyTo(data);string error=process.StandardError.ReadToEnd();
            if(!process.WaitForExit(30000)) {process.Kill();throw new Exception("Worker timeout");}
            Assert((process.ExitCode==0)==success,Path.GetFileName(path)+" status "+(success?"decoded":"rejected")+(success&&process.ExitCode!=0?" "+error:""));
            if(success) {
                data.Position=0;using(var reader=new BinaryReader(data,Encoding.UTF8,true)) {
                    Assert(reader.ReadString()=="QPIMG1","protocol");
                    Assert(reader.ReadInt32()==128 && reader.ReadInt32()==96,"original dimensions");
                    Assert(reader.ReadInt32()==count,"page count");reader.ReadString();
                    byte[] png=reader.ReadBytes((int)(data.Length-data.Position));
                    using(var image=new MagickImage(png,MagickFormat.Png)) {
                        Assert(image.Width==(edge==0?128:64) && image.Height==(edge==0?96:48),edge==0?"full resolution":"bounded preview");
                        if(Path.GetFileName(path)=="layers.xcf") {var pixel=image.GetPixels().GetPixel(0,0).ToColor();Assert(pixel.R>30 && pixel.B>30 && pixel.G<10,"XCF combines red and blue layers ("+pixel+")");}
                    }
                }
            }
        }
        Assert(Convert.ToBase64String(before)==Convert.ToBase64String(Hash(path)),"source unchanged");
    }
    // Small RLE XCF v0 fixture: two full-canvas RGBA layers.
    // Offsets, big-endian properties and tile hierarchy follow GIMP's XCF format.
    static void WriteXcf(string path) {
        using(var data=new MemoryStream())using(var writer=new BinaryWriter(data)) {
            writer.Write(Encoding.ASCII.GetBytes("gimp xcf file\0")); U(writer,128);U(writer,96);U(writer,0);
            U(writer,17);U(writer,1);writer.Write((byte)1);U(writer,0);U(writer,0);
            long layerTable=data.Position;U(writer,0);U(writer,0);U(writer,0);U(writer,0);
            for(int layer=0;layer<2;layer++) {
                Patch(writer,layerTable+layer*4,(uint)data.Position);
                U(writer,128);U(writer,96);U(writer,1);U(writer,6);writer.Write(Encoding.ASCII.GetBytes("layer\0"));
                U(writer,8);U(writer,4);U(writer,1); // visible
                U(writer,6);U(writer,4);U(writer,255U); // opacity (top pixels have partial alpha)
                U(writer,0);U(writer,0);
                long hierarchy=data.Position;U(writer,0);U(writer,0);Patch(writer,hierarchy,(uint)data.Position);
                U(writer,128);U(writer,96);U(writer,4);long level=data.Position;U(writer,0);U(writer,0);Patch(writer,level,(uint)data.Position);
                U(writer,128);U(writer,96);long tiles=data.Position;for(int i=0;i<5;i++)U(writer,0);
                for(int tile=0;tile<4;tile++) {
                    Patch(writer,tiles+tile*4,(uint)data.Position);
                    int height=tile<2?64:32;
                    byte[] planes={(byte)(layer==0?0:255),0,(byte)(layer==0?255:0),(byte)(layer==0?128:255)};
                    foreach(byte value in planes){writer.Write((byte)127);writer.Write((byte)((64*height)>>8));writer.Write((byte)((64*height)&255));writer.Write(value);}
                }
                // The decoder reads a full maximum-tile span for the last tile.
                writer.Write(new byte[24576]);
            }
            File.WriteAllBytes(path,data.ToArray());
        }
    }
    static void U(BinaryWriter w,uint value) {w.Write(new[]{(byte)(value>>24),(byte)(value>>16),(byte)(value>>8),(byte)value});}
    static void Patch(BinaryWriter w,long offset,uint value) {long p=w.BaseStream.Position;w.BaseStream.Position=offset;U(w,value);w.BaseStream.Position=p;}
}
