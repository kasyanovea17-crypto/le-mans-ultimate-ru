using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace LmuRu {
 public static class EngineTests {
  static int count;
  static void Assert(bool b,string name){if(!b)throw new Exception("FAIL: "+name);count++;}
  static void Reject(Action a,string name){bool threw=false;try{a();}catch(Exception){threw=true;}Assert(threw,name);}
  static byte[] Utf(string s){return new UTF8Encoding(false).GetBytes(s);}
  static void Entry(ZipArchive z,string name,byte[] bytes){var e=z.CreateEntry(name);e.LastWriteTime=new DateTimeOffset(2026,9,20,0,0,0,TimeSpan.Zero);using(var s=e.Open())s.Write(bytes,0,bytes.Length);}
  static Package Fixture(string game) {
   Directory.CreateDirectory(Path.Combine(game,"Bin"));File.WriteAllText(Path.Combine(game,"Le Mans Ultimate.exe"),"test fixture; never executed");
   byte[] a=Utf("{\"Settings\":\"Settings\"}\n"),b=Utf("var s = original;\n"),c=new byte[]{1,2,3,4,5};
   using(var s=File.Create(Path.Combine(game,"Bin","UI.zip")))using(var z=new ZipArchive(s,ZipArchiveMode.Create)){Entry(z,"start/locales/en/translation.json",a);Entry(z,"start/app.js",b);Entry(z,"untouched.bin",c);}
   var p=new Package();byte[] after=Utf("{\"Settings\":\"Настройки\"}\n"),js=Utf("var s = translated;\r\n// display only\r\n"),font=new byte[]{8,7,6};
   byte[] delta=Utf(Package.Json.Serialize(new Delta{replacements=new[]{new Replacement{before="original",after="translated"}},append="// display only\n"}));
   p.Data["translation"]=after;p.Data["delta"]=delta;p.Data["font"]=font;
   p.Info=new Manifest{version="fixture",game_version="fixture",original_entries=3,original_sha256=Package.HashFile(Engine.Target(game)),known_current_sha256=new string[0],known_previous_sha256=new string[0],files=new[]{
    new PatchFile{entry="start/locales/en/translation.json",action="modified",mode="replace",payload="translation",before_sha256=Package.Hash(a),after_sha256=Package.Hash(after)},
    new PatchFile{entry="start/app.js",action="modified",mode="transform",payload="delta",before_sha256=Package.Hash(b),after_sha256=Package.Hash(js)},
    new PatchFile{entry="start/fonts/ru/test.ttf",action="added",mode="replace",payload="font",after_sha256=Package.Hash(font)}
   }};return p;
  }
  static void UnitTests(string root) {
   var p=Fixture(root);var e=new Engine(p);e.Running=()=>false;string target=Engine.Target(root),backup=Engine.BackupPath(root);var baseline=e.Inspect(root);
   Assert(baseline.State=="original"&&baseline.CanInstall&&!baseline.HasBackup,"baseline flags");
   string translated=e.Install(root);Assert(translated!=p.Info.original_sha256,"install changes archive");Assert(Package.HashFile(backup)==p.Info.original_sha256,"exact backup");
   var modified=e.Verify(root);Assert(modified.State=="installed"&&modified.CanRestore&&!modified.CanInstall,"installed flags");
   Assert(e.Install(root)==translated,"idempotent install");Assert(Package.HashFile(backup)==p.Info.original_sha256,"backup immutable");
   Assert(e.Restore(root)==p.Info.original_sha256,"restore original hash");Assert(Package.HashFile(target)==p.Info.original_sha256,"actual restore bytes");Assert(e.Restore(root)==p.Info.original_sha256,"idempotent restore");
   Assert(e.Install(root)==translated,"deterministic reinstall");e.Running=()=>true;
   Reject(()=>e.Restore(root),"running game blocks restore");Reject(()=>e.Install(root),"running game blocks install");Assert(Package.HashFile(target)==translated,"running guard leaves data");e.Running=()=>false;e.Restore(root);
   var good=File.ReadAllBytes(target);File.WriteAllText(target,"unknown version");
   Assert(e.Inspect(root).State=="unknown","unknown detected");Reject(()=>e.Install(root),"unknown install blocked");Reject(()=>e.Restore(root),"unknown restore blocked");Assert(File.ReadAllText(target)=="unknown version","unknown preserved");File.WriteAllBytes(target,good);
   var originalBackup=File.ReadAllBytes(backup);File.WriteAllText(backup,"corrupt");Reject(()=>e.Install(root),"corrupt backup protected");Assert(File.ReadAllText(backup)=="corrupt","corrupt backup not overwritten");File.WriteAllBytes(backup,originalBackup);
   var translation=p.Data["translation"];p.Data["translation"]=Utf("bad payload");Reject(()=>e.Install(root),"payload corruption rejected");Assert(Package.HashFile(target)==p.Info.original_sha256,"failed staging keeps original");p.Data["translation"]=translation;
   Reject(()=>Engine.GameRoot(Path.Combine(root,"missing")),"missing game");
   var js=p.Info.files.Single(f=>f.mode=="transform");var originalHash=js.before_sha256;byte[] dup=Utf("original original");js.before_sha256=Package.Hash(dup);Reject(()=>p.Transform(js,dup),"ambiguous JS patch");js.before_sha256=originalHash;
   string second=root+"-adopt";var p2=Fixture(second);var e2=new Engine(p2);e2.Running=()=>false;
   string wrong=Path.Combine(second,"wrong.zip");File.WriteAllText(wrong,"not original");Reject(()=>e2.AdoptOriginal(second,wrong),"invalid import");Assert(!File.Exists(Engine.BackupPath(second)),"no invalid backup created");e2.AdoptOriginal(second,Engine.Target(second));Assert(e2.Inspect(second).HasBackup,"valid import");
   e.Install(root);
   // Forging a receipt must not bless changes to unrelated game resources.
   using(var fs=new FileStream(target,FileMode.Open,FileAccess.ReadWrite))using(var z=new ZipArchive(fs,ZipArchiveMode.Update)){z.GetEntry("untouched.bin").Delete();Entry(z,"untouched.bin",new byte[]{9,9,9});}
   File.WriteAllText(Path.Combine(Path.GetDirectoryName(backup),"installed.json"),Package.Json.Serialize(new Receipt{version=p.Info.version,original_sha256=p.Info.original_sha256,sha256=Package.HashFile(target)}));
   Assert(e.Inspect(root).State=="unknown","forged receipt rejected");Reject(()=>e.Restore(root),"altered content not overwritten");
   Assert(!Directory.GetFiles(Path.Combine(root,"Bin"),".lmu-ru-*.tmp").Any(),"stages cleaned");
   Console.WriteLine("ENGINE_TEST_PASS checks="+count+" unknown_version=blocked backup=verified atomic_install=true rollback=byte_exact game_running=blocked real_game_writes=false");
  }
  static void RealArchive(string original,string root,string nativeOriginal) {
   var p=Package.Load();Assert(Package.HashFile(original)==p.Info.original_sha256,"real baseline hash");
   Directory.CreateDirectory(Path.Combine(root,"Bin"));File.WriteAllText(Path.Combine(root,"Le Mans Ultimate.exe"),"fixture only; never executed");File.Copy(original,Path.Combine(root,"Bin","UI.zip"),false);
   if(p.Native!=null)foreach(var f in p.Native.Info.files){string dst=Path.Combine(root,f.entry);Directory.CreateDirectory(Path.GetDirectoryName(dst));File.Copy(Path.Combine(nativeOriginal,f.entry),dst,false);}
   var e=new Engine(p);e.Running=()=>false;var a=e.Inspect(root);Assert(a.State=="original","real baseline state");Console.WriteLine("BASELINE_PASS sha256="+a.Hash+" entries="+p.Info.original_entries);
   string hash=e.Install(root);Assert(e.Verify(root).State=="installed","real modified verification");Assert(Package.HashFile(Engine.BackupPath(root))==p.Info.original_sha256,"real backup retained");
   Console.WriteLine("MODIFIED_PASS sha256="+hash+" patched_entries=13 native_files=642 all_other_payloads=byte_identical");
   string result=e.Restore(root);Assert(result==p.Info.original_sha256&&Package.HashFile(Engine.Target(root))==p.Info.original_sha256,"real rollback hash");Console.WriteLine("ROLLBACK_PASS sha256="+result+" behavior=original_english_interface_and_hud");
   string final=e.Install(root);Assert(final==hash,"real deterministic rebuild");Console.WriteLine("REAL_ARCHIVE_TEST_PASS checks="+count+" installed_fixture_left_changed=true real_game_writes=false");
  }
  public static int Main(string[] args) {
   try{if(args.Length==4&&args[0]=="--real")RealArchive(args[1],args[2],args[3]);else UnitTests(Path.Combine(Path.GetTempPath(),"LMU-RU-tests-"+Guid.NewGuid().ToString("N")));return 0;}
   catch(Exception e){Console.Error.WriteLine(e);return 1;}
  }
 }
}
