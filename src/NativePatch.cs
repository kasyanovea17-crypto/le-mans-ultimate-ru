using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LmuRu {
 public sealed class NativeManifest {
  public string version; public int translated_strings,dictionary_entries,font_files,preserved_glyphs;
  public PatchFile[] files;
 }
 public sealed class NativeState {
  public int Originals,Installed,Previous; public bool CanRestore;
  public string State {get{return Installed==0&&Previous==0?"original":Originals==0&&Previous==0?"installed":"partial";}}
 }
 public sealed class NativePackage {
  public NativeManifest Info; public readonly Dictionary<string,byte[]> Data=new Dictionary<string,byte[]>();
  internal Action<int> BeforeCommit=delegate{};
  public static NativePackage Load(byte[] bytes) {
   var p=new NativePackage();
   using(var s=new MemoryStream(bytes))using(var z=new ZipArchive(s,ZipArchiveMode.Read)) {
    p.Info=Package.Json.Deserialize<NativeManifest>(Encoding.UTF8.GetString(Package.Read(z.GetEntry("manifest.json"))));
    if(p.Info.files.Length<1||p.Info.files.Length>2000)throw new InvalidDataException("Неверный список ресурсов HUD.");
    var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach(var f in p.Info.files) {
     if(!paths.Add(f.entry)||!(f.entry=="Support/Languages/english.dic"||Regex.IsMatch(f.entry,@"\ACore/Shared/SpriteFonts/[A-Za-z0-9_]+\.spritefont\z")))throw new InvalidDataException("Недопустимый путь HUD.");
     var data=Package.Read(z.GetEntry(f.payload));if(Package.Hash(data)!=f.payload_sha256)throw new InvalidDataException("Повреждён пакет HUD.");p.Data.Add(f.payload,data);
    }
   }
   return p;
  }
  static string PathFor(string root,string relative) {
   root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
   string p=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
   if(!p.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Путь вне папки игры.");
   for(string q=p; q!=null && q.Length>=root.Length-1; q=Path.GetDirectoryName(q))
    if((File.Exists(q)||Directory.Exists(q))&&(File.GetAttributes(q)&FileAttributes.ReparsePoint)!=0)throw new IOException("Ресурс HUD находится по ссылке.");
   return p;
  }
  static string Backup(string root,PatchFile f){return PathFor(root,"Bin/LMU-RU-backup/native/"+f.entry);}
  public NativeState Inspect(string root) {
   var s=new NativeState{CanRestore=true};
   foreach(var f in Info.files) {
    string target=PathFor(root,f.entry),backup=Backup(root,f);
    if(!File.Exists(target))throw new InvalidDataException("Не найден ресурс HUD: "+f.entry);
    string hash=Package.HashFile(target);
    if(hash==f.before_sha256)s.Originals++;else if(hash==f.after_sha256)s.Installed++;else if(f.previous_sha256!=null&&f.previous_sha256.Contains(hash))s.Previous++;else throw new InvalidDataException("Неизвестная версия ресурса HUD: "+f.entry);
    if(File.Exists(backup)) {
     if(Package.HashFile(backup)!=f.before_sha256)throw new InvalidDataException("Повреждена резервная копия HUD: "+f.entry);
    }else if(hash!=f.before_sha256)s.CanRestore=false;
   }
   return s;
  }
  public byte[] Transform(PatchFile f,byte[] original) {
   if(Package.Hash(original)!=f.before_sha256)throw new InvalidDataException("Неверный исходный ресурс HUD.");
   byte[] result;
   if(f.mode=="replace")result=Data[f.payload];
   else if(f.mode=="spritefont")using(var stream=new MemoryStream(Data[f.payload]))using(var r=new BinaryReader(stream))using(var output=new MemoryStream()) {
    if(Encoding.ASCII.GetString(r.ReadBytes(8))!="LMURUFT1")throw new InvalidDataException("Неверный формат шрифта HUD.");
    int prefix=r.ReadInt32(),offset=r.ReadInt32(),oldStride=r.ReadInt32(),rows=r.ReadInt32(),stride=r.ReadInt32();
    if(prefix<40||prefix>1024*1024||offset<40||oldStride<=0||rows<=0||stride<oldStride||stride>65536||(long)offset+(long)oldStride*rows>original.Length||(long)stride*rows>64*1024*1024)throw new InvalidDataException("Неверные размеры шрифта.");
    byte[] header=r.ReadBytes(prefix);if(header.Length!=prefix)throw new InvalidDataException("Обрезан заголовок шрифта.");output.Write(header,0,header.Length);
    byte[] padding=new byte[stride-oldStride];
    for(int row=0;row<rows;row++){output.Write(original,offset+row*oldStride,oldStride);output.Write(padding,0,padding.Length);}
    stream.CopyTo(output);int tail=offset+oldStride*rows;output.Write(original,tail,original.Length-tail);result=output.ToArray();
   }else throw new InvalidDataException("Неизвестный тип ресурса HUD.");
   if(Package.Hash(result)!=f.after_sha256)throw new InvalidDataException("Ошибка сборки HUD: "+f.entry);
   return result;
  }
  public void Apply(string root,bool install,Action guard,Action<int,string> progress) {
   guard();var initial=Inspect(root);
   if(!initial.CanRestore)throw new InvalidDataException("Для HUD отсутствует исходная резервная копия. Восстановите оригинальные ресурсы через Steam.");
   string txn=PathFor(root,"Bin/LMU-RU-backup/.native-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(txn);
   bool complete=false;var prepared=new List<Tuple<PatchFile,string,string,string>>();var committed=new List<Tuple<PatchFile,string,string,string>>();
   try {
    // Stage the complete native set and retain every original before the first replacement.
    foreach(var f in Info.files) {
     string target=PathFor(root,f.entry),backup=Backup(root,f),current=Package.HashFile(target),desired=install?f.after_sha256:f.before_sha256;
     if(current==desired)continue;
     if(!File.Exists(backup)) {
      if(current!=f.before_sha256)throw new InvalidDataException("Исходный ресурс HUD отсутствует.");
      Directory.CreateDirectory(Path.GetDirectoryName(backup));string temp=backup+"."+Guid.NewGuid().ToString("N")+".tmp";
      try{File.Copy(target,temp);if(Package.HashFile(temp)!=f.before_sha256)throw new IOException("Ошибка резервного копирования HUD.");File.Move(temp,backup);}finally{if(File.Exists(temp))File.Delete(temp);}
     }
     byte[] original=File.ReadAllBytes(backup);if(Package.Hash(original)!=f.before_sha256)throw new InvalidDataException("Резервная копия HUD изменилась.");
     string stage=Path.Combine(txn,prepared.Count+".new"),prior=Path.Combine(txn,prepared.Count+".prior");
     File.WriteAllBytes(stage,install?Transform(f,original):original);
     if(Package.HashFile(stage)!=desired)throw new InvalidDataException("Ошибка промежуточного файла HUD.");
     prepared.Add(Tuple.Create(f,current,stage,prior));
    }
    guard();int i=0;
    foreach(var item in prepared) {
     guard();BeforeCommit(i);string target=PathFor(root,item.Item1.entry);
     if(Package.HashFile(target)!=item.Item2)throw new IOException("Ресурс HUD изменился во время установки.");
     File.Replace(item.Item3,target,item.Item4);committed.Add(item);
     if(Package.HashFile(target)!=(install?item.Item1.after_sha256:item.Item1.before_sha256))throw new IOException("Итоговая проверка HUD не пройдена.");
     if(++i%40==0)progress(96,"HUD: "+i+" / "+prepared.Count);
    }
    var end=Inspect(root);if(end.State!=(install?"installed":"original"))throw new IOException("Изменены не все ресурсы HUD.");complete=true;
   }catch(Exception operationError) {
    // Exception rollback uses the actual pre-transaction files, including a mixed/recovered state.
    var failures=new List<Exception>();
    foreach(var item in Enumerable.Reverse(committed))try {
     string target=PathFor(root,item.Item1.entry),desired=install?item.Item1.after_sha256:item.Item1.before_sha256;
     if(Package.HashFile(target)!=desired||Package.HashFile(item.Item4)!=item.Item2)throw new IOException("Файл изменён извне; копия оставлена: "+item.Item4);
     File.Replace(item.Item4,target,null);if(Package.HashFile(target)!=item.Item2)throw new IOException("Ошибка отката HUD.");
    }catch(Exception e){failures.Add(e);}
    if(failures.Count>0){failures.Insert(0,operationError);throw new AggregateException("Операция HUD прервана; сохранены копии: "+txn,failures);}
    throw;
   }finally {
    // Never recursively remove a transaction directory: retain any unrecovered .prior file.
    foreach(var item in prepared)if(File.Exists(item.Item3))File.Delete(item.Item3);
    bool success=complete;
    if(success)foreach(var item in committed) {
     string expected=install?item.Item1.after_sha256:item.Item1.before_sha256;
     if(File.Exists(item.Item4)&&Package.HashFile(PathFor(root,item.Item1.entry))==expected&&Package.HashFile(item.Item4)==item.Item2)File.Delete(item.Item4);
    }
    if(!Directory.EnumerateFileSystemEntries(txn).Any())Directory.Delete(txn);
   }
  }
 }
}
