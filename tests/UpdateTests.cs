using System;
using System.IO;
using System.IO.Compression;
using System.Text;
namespace LmuRu {
 public static class UpdateTests {
  static int count;
  static void Assert(bool value){if(!value)throw new Exception("Update assertion "+count);count++;}
  static void Reject(Action action){bool failed=false;try{action();}catch(Exception){failed=true;}Assert(failed);}
  public static int Main(string[] args){try {
   if(args.Length==2&&args[0]=="--live") {var live=Updates.Check(args[1]);Console.WriteLine(live==null?"UPDATE_LIVE_PASS current=true":"UPDATE_LIVE_PASS version="+live.Version+" sha256="+live.Hash);return 0;}
   var asset=new ReleaseAsset{name="LMU-RU-Launcher-1.2.0-win-x64.zip",browser_download_url="https://github.com/kasyanovea17-crypto/le-mans-ultimate-ru/releases/download/v1.2.0/LMU-RU-Launcher-1.2.0-win-x64.zip",size=3,digest="sha256:"+new string('a',64)};
   var release=new ReleaseInfo{tag_name="v1.2.0",assets=new[]{asset}};
   Func<string> json=()=>Package.Json.Serialize(release);
   Assert(Updates.Parse(json(),"1.1.0").Version=="1.2.0");Assert(Updates.Parse(json(),"1.2.0")==null);Assert(Updates.Parse(json(),"1.10.0")==null);
   release.prerelease=true;Reject(()=>Updates.Parse(json(),"1.1.0"));release.prerelease=false;
   asset.browser_download_url="https://evil.example/file.zip";Reject(()=>Updates.Parse(json(),"1.1.0"));
   asset.browser_download_url="https://github.com/kasyanovea17-crypto/le-mans-ultimate-ru/releases/download/v1.2.0/"+asset.name;
   asset.digest=null;Reject(()=>Updates.Parse(json(),"1.1.0"));asset.digest="sha256:"+new string('a',64);
   asset.size=999999999;Reject(()=>Updates.Parse(json(),"1.1.0"));asset.size=3;
   Reject(()=>Updates.ParseVersion("1.1.0/../../x"));Reject(()=>Updates.ParseVersion("1.1"));
   byte[] bytes;
   using(var m=new MemoryStream()){using(var z=new ZipArchive(m,ZipArchiveMode.Create,true)) {using(var s=z.CreateEntry("LMU-RU-Launcher.exe").Open()){var b=Encoding.ASCII.GetBytes("MZ fixture never executed");s.Write(b,0,b.Length);}using(var s=z.CreateEntry("../../outside.txt").Open()){s.WriteByte(1);}}bytes=m.ToArray();}
   var offer=new UpdateOffer{Version="1.2.0",Hash=Package.Hash(bytes),Size=bytes.Length};
   string root=Path.Combine(Path.GetTempPath(),"LMU-update-test-"+Guid.NewGuid().ToString("N"));string exe=Updates.Unpack(offer,bytes,root);
   Assert(File.Exists(exe));Assert(Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length==1);
   bytes[0]^=1;Reject(()=>Updates.Unpack(offer,bytes,root));Assert(File.Exists(exe));
   Console.WriteLine("UPDATE_TEST_PASS checks="+count+" downgrade=blocked foreign_url=blocked missing_hash=blocked corrupt_download=blocked zip_paths=ignored real_game_writes=false");return 0;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
 }
}

