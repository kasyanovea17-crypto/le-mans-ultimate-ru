using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LmuRu {
 public static class NativeTests {
  static int count;
  static void Check(bool v,string message){if(!v)throw new Exception(message);count++;}
  static void Reject(Action a,string message){bool fail=false;try{a();}catch(Exception){fail=true;}Check(fail,message);}
  static void Unit() {
   string root=Path.Combine(Path.GetTempPath(),"LMU-HUD-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   var p=new NativePackage();var list=new List<PatchFile>();
   foreach(string name in new[]{"Support/Languages/english.dic","Core/Shared/SpriteFonts/TEST.spritefont"}) {
    byte[] before=Encoding.UTF8.GetBytes("original "+name),after=Encoding.UTF8.GetBytes("перевод "+name);
    string target=Path.Combine(root,name);Directory.CreateDirectory(Path.GetDirectoryName(target));File.WriteAllBytes(target,before);
    p.Data[name]=after;list.Add(new PatchFile{entry=name,payload=name,mode="replace",before_sha256=Package.Hash(before),after_sha256=Package.Hash(after)});
   }
   p.Info=new NativeManifest{files=list.ToArray()};Action guard=delegate{};Action<int,string> progress=delegate{};
   Check(p.Inspect(root).State=="original","native baseline");
   p.BeforeCommit=n=>{if(n==1)throw new IOException("injected second-file failure");};
   Reject(()=>p.Apply(root,true,guard,progress),"injected failure");Check(p.Inspect(root).State=="original","exception restores first file");
   p.BeforeCommit=delegate{};p.Apply(root,true,guard,progress);Check(p.Inspect(root).State=="installed","native install");Check(p.Inspect(root).CanRestore,"native backups");
   p.Apply(root,true,guard,progress);Check(p.Inspect(root).State=="installed","native idempotent install");
   Reject(()=>p.Apply(root,false,()=>{throw new IOException("game running");},progress),"native running guard");Check(p.Inspect(root).State=="installed","guard preserves");
   p.BeforeCommit=n=>{if(n==1)throw new IOException("injected restore failure");};Reject(()=>p.Apply(root,false,guard,progress),"restore failure injected");Check(p.Inspect(root).State=="installed","restore failure rolls back");p.BeforeCommit=delegate{};
   p.Apply(root,false,guard,progress);Check(p.Inspect(root).State=="original","native exact restore");p.Apply(root,false,guard,progress);Check(p.Inspect(root).State=="original","native idempotent restore");
   // A crash can leave a known mixed state: rerunning installs the remaining files.
   File.WriteAllBytes(Path.Combine(root,list[0].entry),p.Data[list[0].payload]);Check(p.Inspect(root).State=="partial","mixed state detected");p.Apply(root,true,guard,progress);Check(p.Inspect(root).State=="installed","mixed state repaired");
   string altered=Path.Combine(root,list[0].entry);File.WriteAllText(altered,"external change");Reject(()=>p.Apply(root,true,guard,progress),"unknown native blocks install");Reject(()=>p.Apply(root,false,guard,progress),"unknown native blocks restore");Check(File.ReadAllText(altered)=="external change","unknown retained");
   File.WriteAllBytes(altered,p.Data[list[0].payload]);string backup=Path.Combine(root,"Bin/LMU-RU-backup/native",list[0].entry);File.WriteAllText(backup,"bad backup");Reject(()=>p.Apply(root,false,guard,progress),"corrupt backup blocks");Check(File.ReadAllText(backup)=="bad backup","bad backup retained");
   Console.WriteLine("NATIVE_UNIT_PASS checks="+count+" injected_install_failure=rolled_back injected_restore_failure=rolled_back mixed_state=repaired unknown_files=preserved");
  }
  static void Real(string originals,string root) {
   var p=Package.Load().Native;if(p==null)throw new Exception("No native package");
   if(Directory.Exists(root))throw new Exception("Fixture must be new");Directory.CreateDirectory(root);
   foreach(var f in p.Info.files){string src=Path.Combine(originals,f.entry),dst=Path.Combine(root,f.entry);Directory.CreateDirectory(Path.GetDirectoryName(dst));File.Copy(src,dst);Check(Package.HashFile(dst)==f.before_sha256,"baseline "+f.entry);}
   Console.WriteLine("BASELINE_PASS native_files="+p.Info.files.Length+" state="+p.Inspect(root).State);
   p.Apply(root,true,delegate{},delegate{});Check(p.Inspect(root).State=="installed","real native installed");
   Console.WriteLine("MODIFIED_PASS native_files="+p.Info.files.Length+" translated_strings="+p.Info.translated_strings+" state=installed");
   p.Apply(root,false,delegate{},delegate{});Check(p.Inspect(root).State=="original","real native rollback");
   Console.WriteLine("ROLLBACK_PASS native_files="+p.Info.files.Length+" hashes=byte_exact state=original");
   p.Apply(root,true,delegate{},delegate{});Check(p.Inspect(root).State=="installed","leave changed");
   Console.WriteLine("NATIVE_REAL_PASS checks="+count+" installed_fixture_left_changed=true real_game_writes=false");
  }
  public static int Main(string[] args){try{if(args.Length==3&&args[0]=="--real")Real(args[1],args[2]);else Unit();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
 }
}
