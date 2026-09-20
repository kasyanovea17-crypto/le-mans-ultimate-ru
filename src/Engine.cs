using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace LmuRu {
 public sealed class PatchFile {
  public string entry, action, mode, payload, before_sha256, after_sha256, payload_sha256;
  public string[] previous_sha256;
 }
 public sealed class Manifest {
  public string version, game_version, original_sha256, native_payload, native_sha256;
  public int original_entries, reviewed_strings, russian_strings, preserved_special;
  public string[] known_current_sha256, known_previous_sha256;
  public PatchFile[] files;
 }
 public sealed class Replacement { public string before, after; }
 public sealed class Delta { public Replacement[] replacements; public string append; }
 public sealed class Receipt { public string version, sha256, original_sha256; }
 public sealed class Inspection {
  public string State, Hash, Backup, Message;
  public bool HasBackup, CanInstall, CanRestore, CanLaunch;
 }
 public sealed class Package {
  public Manifest Info; public NativePackage Native; public Dictionary<string,byte[]> Data = new Dictionary<string,byte[]>();
  public static JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength=32*1024*1024 };
  public static Package Load() {
   var p=new Package();
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("LMU.Payload")) {
    if(stream==null)throw new InvalidDataException("Пакет перевода отсутствует.");
    using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)) {
     var manifest=Read(zip.GetEntry("manifest.json"));
     if(Hash(manifest)!=BuildInfo.ManifestHash)throw new InvalidDataException("Манифест пакета повреждён.");
     p.Info=Json.Deserialize<Manifest>(Encoding.UTF8.GetString(manifest));
     if(!string.IsNullOrEmpty(p.Info.native_payload)){var bytes=Read(zip.GetEntry(p.Info.native_payload));if(Hash(bytes)!=p.Info.native_sha256)throw new InvalidDataException("Повреждён пакет HUD.");p.Native=NativePackage.Load(bytes);}
     foreach(var f in p.Info.files) {
      byte[] bytes=Read(zip.GetEntry(f.payload));
      if(Hash(bytes)!=f.payload_sha256)throw new InvalidDataException("Повреждён ресурс: "+f.payload);
      p.Data.Add(f.payload,bytes);
     }
    }
   }
   if(p.Info.files.Select(f=>f.entry).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=p.Info.files.Length)throw new InvalidDataException("Повторяющиеся ресурсы.");
   return p;
  }
  public static byte[] Read(ZipArchiveEntry e) {
   if(e==null || e.Length>32*1024*1024)throw new InvalidDataException("Не найден или слишком велик ресурс пакета.");
   using(var s=e.Open())using(var m=new MemoryStream()){s.CopyTo(m);return m.ToArray();}
  }
  public static string Hash(byte[] bytes) { using(var h=SHA256.Create())return Hex(h.ComputeHash(bytes)); }
  public static string HashFile(string file) { using(var s=File.OpenRead(file))return HashStream(s); }
  public static string HashStream(Stream s) {using(var h=SHA256.Create())return Hex(h.ComputeHash(s));}
  static string Hex(byte[] b){return BitConverter.ToString(b).Replace("-","").ToLowerInvariant();}
  public byte[] Transform(PatchFile f,byte[] original) {
   if(f.mode=="replace")return Data[f.payload];
   if(f.mode!="transform" || original==null)throw new InvalidDataException("Неизвестный тип изменения.");
   if(Hash(original)!=f.before_sha256)throw new InvalidDataException("Изменилась исходная структура: "+f.entry);
   string text=new UTF8Encoding(false,true).GetString(original).TrimStart('\uFEFF').Replace("\r\n","\n");
   var delta=Json.Deserialize<Delta>(Encoding.UTF8.GetString(Data[f.payload]));
   foreach(var r in delta.replacements) {
    if(string.IsNullOrEmpty(r.before) || text.IndexOf(r.before,StringComparison.Ordinal)<0 || text.IndexOf(r.before,StringComparison.Ordinal)!=text.LastIndexOf(r.before,StringComparison.Ordinal))throw new InvalidDataException("Неоднозначный участок: "+f.entry);
    text=text.Replace(r.before,r.after);
   }
   return new UTF8Encoding(false).GetBytes((text+delta.append).Replace("\n","\r\n"));
  }
 }
 public sealed class Engine {
  public readonly Package Pack;
  public Action<int,string> Progress = delegate {};
  internal Func<bool> Running = GameRunning;
  public Engine(Package p){Pack=p;}
  public static bool GameRunning() {
   foreach(string n in new[]{"Le Mans Ultimate","Launch Le Mans Ultimate","start_protected_game","rFactor2"}) {
    var list=Process.GetProcessesByName(n);try{if(list.Length>0)return true;}finally{foreach(var p in list)p.Dispose();}
   }
   return false;
  }
  public static string GameRoot(string game) {
   if(string.IsNullOrWhiteSpace(game))throw new DirectoryNotFoundException("Выберите папку Le Mans Ultimate.");
   string root=Path.GetFullPath(game.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar);
   if(!File.Exists(Path.Combine(root,"Le Mans Ultimate.exe")) || !File.Exists(Path.Combine(root,"Bin","UI.zip")))throw new DirectoryNotFoundException("В выбранной папке нет Le Mans Ultimate.exe и Bin\\UI.zip.");
   return root;
  }
  public static string Target(string game){return Path.Combine(GameRoot(game),"Bin","UI.zip");}
  public static string BackupPath(string game){return Path.Combine(GameRoot(game),"Bin","LMU-RU-backup","UI.original.zip");}
  static string ReceiptPath(string game){return Path.Combine(GameRoot(game),"Bin","LMU-RU-backup","installed.json");}
  static void PlainPath(string p) {
   if((File.Exists(p)||Directory.Exists(p)) && (File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Путь является ссылкой: выберите обычную папку игры.");
  }
  void Guard(string game) {
   if(Running())throw new IOException("Сначала закройте Le Mans Ultimate и её лаунчер.");
   string root=GameRoot(game);PlainPath(root);PlainPath(Path.Combine(root,"Bin"));PlainPath(Target(root));PlainPath(Path.GetDirectoryName(BackupPath(root)));PlainPath(BackupPath(root));PlainPath(ReceiptPath(root));
  }
  Inspection InspectMenu(string game) {
   string target=Target(game),backup=BackupPath(game);Progress(5,"Проверяем контрольную сумму UI.zip…");
   string hash=Package.HashFile(target);
   bool hasBackup=File.Exists(backup) && Package.HashFile(backup)==Pack.Info.original_sha256;
   bool current=Pack.Info.known_current_sha256.Contains(hash), previous=Pack.Info.known_previous_sha256.Contains(hash);
   string receiptPath=ReceiptPath(game);
   if(!current && File.Exists(receiptPath)) {
    try {
     if(new FileInfo(receiptPath).Length>65536)throw new InvalidDataException();
     var r=Package.Json.Deserialize<Receipt>(File.ReadAllText(receiptPath));
     if(r!=null && r.original_sha256==Pack.Info.original_sha256 && r.sha256==hash && r.version==Pack.Info.version) {
      // A receipt alone is not a compatibility proof: compare all payloads to the verified original.
      if(hasBackup){VerifyArchive(target,backup);current=true;}
     }
    }catch(InvalidDataException){}catch(ArgumentException){}
   }
   bool original=hash==Pack.Info.original_sha256;
   string state=original?"original":current?"installed":previous?"previous":"unknown";
   return new Inspection { State=state,Hash=hash,Backup=backup,HasBackup=hasBackup,
    CanInstall=original || (hasBackup && previous),CanRestore=hasBackup&&(current||previous),CanLaunch=original||current||previous,
    Message=original?"Оригинальная версия совместима с переводом.":current?(hasBackup?"Русский перевод установлен. Оригинал сохранён.":"Перевод установлен. Укажите исходный UI.zip для восстановления."):previous?"Установлена предыдущая редакция перевода.":"Ресурсы игры отличаются от проверенной версии. Дождитесь совместимого перевода." };
  }
  public Inspection Inspect(string game) {
   var s=InspectMenu(game);if(Pack.Native==null||s.State=="unknown")return s;
   try {
    var n=Pack.Native.Inspect(GameRoot(game));
    bool menuOriginal=s.State=="original",menuInstalled=s.State=="installed";
    s.CanRestore=(s.CanRestore||menuOriginal)&&n.CanRestore&&(n.Installed+n.Previous>0||!menuOriginal);
    if(menuInstalled&&n.State!="installed") {
     s.State="previous";s.CanInstall=n.CanRestore;
     s.Message="Меню переведено. Установите дополнение HUD и гоночные шрифты.";
    }else if(menuOriginal&&n.State!="original") {
     s.State="previous";s.CanInstall=n.CanRestore;s.Message="Установлен только HUD. Можно завершить установку или восстановить оригинал.";
    }else if(menuInstalled)s.Message="Меню и HUD установлены. Проверены словари и кириллица.";
   }catch(InvalidDataException ex){s.State="unknown";s.CanInstall=false;s.CanRestore=false;s.Message=ex.Message;}
   return s;
  }
  void NativeApply(string game,bool install){if(Pack.Native!=null)Pack.Native.Apply(GameRoot(game),install,()=>Guard(game),Progress);}
  static Dictionary<string,ZipArchiveEntry> Index(ZipArchive z) {
   var d=new Dictionary<string,ZipArchiveEntry>(StringComparer.Ordinal);
   foreach(var e in z.Entries) {
    string n=e.FullName.Replace('\\','/');
    if(d.ContainsKey(n))throw new InvalidDataException("Повторяющиеся записи архива.");d.Add(n,e);
   }
   return d;
  }
  public void VerifyArchive(string archive,string original) {
   Progress(80,"Проверяем словари, шрифты и неизменённые ресурсы…");
   var edits=Pack.Info.files.ToDictionary(f=>f.entry,StringComparer.Ordinal);
   using(var fs=File.OpenRead(archive))using(var zip=new ZipArchive(fs,ZipArchiveMode.Read)) {
    var index=Index(zip);
    if(index.Count!=Pack.Info.original_entries+Pack.Info.files.Count(f=>f.action=="added"))throw new InvalidDataException("Неверное число ресурсов архива.");
    foreach(var f in Pack.Info.files) {
     if(!index.ContainsKey(f.entry))throw new InvalidDataException("Отсутствует ресурс: "+f.entry);
     using(var s=index[f.entry].Open())if(Package.HashStream(s)!=f.after_sha256)throw new InvalidDataException("Не совпадает ресурс: "+f.entry);
    }
    if(original!=null)using(var originalStream=File.OpenRead(original))using(var baseline=new ZipArchive(originalStream,ZipArchiveMode.Read)) {
     var baseIndex=Index(baseline);
     if(baseIndex.Count!=Pack.Info.original_entries)throw new InvalidDataException("Неверное число оригинальных ресурсов.");
     foreach(var e in baseIndex) {
      if(!index.ContainsKey(e.Key))throw new InvalidDataException("Потерян оригинальный ресурс: "+e.Key);
      if(edits.ContainsKey(e.Key))continue;
      using(var a=e.Value.Open())using(var b=index[e.Key].Open())if(Package.HashStream(a)!=Package.HashStream(b))throw new InvalidDataException("Изменён посторонний ресурс: "+e.Key);
     }
    }
   }
  }
  public Inspection Verify(string game) {
   var s=Inspect(game);
   if(s.State=="unknown")throw new InvalidDataException(s.Message);
   if(s.State=="installed"||s.State=="previous"&&InspectMenu(game).State=="installed")VerifyArchive(Target(game),s.HasBackup?s.Backup:null);
   Progress(100,"Проверка завершена: файлы целы.");return s;
  }
  Mutex Lock(string game) {
   var mutex=new Mutex(false,"Local\\LMU-RU-"+Package.Hash(Encoding.UTF8.GetBytes(Target(game).ToUpperInvariant())));
   bool owns=false;try{owns=mutex.WaitOne(0);}catch(AbandonedMutexException){owns=true;}
   if(!owns){mutex.Dispose();throw new IOException("Другая операция перевода уже выполняется.");}return mutex;
  }
  static void AtomicText(string path,string text) {
   string stage=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{File.WriteAllText(stage,text,new UTF8Encoding(false));if(File.Exists(path))File.Replace(stage,path,null);else File.Move(stage,path);}
   finally{if(File.Exists(stage))File.Delete(stage);}
  }
  void EnsureBackup(string source,string backup) {
   if(File.Exists(backup)) {
    if(Package.HashFile(backup)!=Pack.Info.original_sha256)throw new InvalidDataException("Резервная копия отличается от оригинала. Она не перезаписана.");return;
   }
   if(Package.HashFile(source)!=Pack.Info.original_sha256)throw new InvalidDataException("Для резервной копии нужен исходный UI.zip версии "+Pack.Info.game_version+".");
   Directory.CreateDirectory(Path.GetDirectoryName(backup));
   string stage=backup+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{File.Copy(source,stage);if(Package.HashFile(stage)!=Pack.Info.original_sha256)throw new InvalidDataException("Ошибка копирования оригинала.");File.Move(stage,backup);}
   finally{if(File.Exists(stage))File.Delete(stage);}
  }
  public void AdoptOriginal(string game,string original) {
   using(var m=Lock(game))try{Guard(game);var s=Inspect(game);if(s.State=="unknown")throw new InvalidDataException(s.Message);EnsureBackup(original,BackupPath(game));Progress(100,"Оригинальная копия сохранена.");}finally{m.ReleaseMutex();}
  }
  void Commit(string game,string stage,string expectedCurrent,string expectedNew) {
   Guard(game);string target=Target(game);
   if(Package.HashFile(target)!=expectedCurrent)throw new IOException("UI.zip изменился во время операции. Повторите проверку.");
   if(Package.HashFile(stage)!=expectedNew)throw new InvalidDataException("Промежуточный архив повреждён.");
   string prior=stage+".previous";
   File.Replace(stage,target,prior);
   if(Package.HashFile(target)!=expectedNew)throw new InvalidDataException("Ошибка итоговой проверки; предыдущий архив сохранён: "+prior);
   if(Package.HashFile(prior)!=expectedCurrent)throw new InvalidDataException("Предыдущий архив отличается; сохранён: "+prior);
   File.Delete(prior);
  }
  public string Install(string game) {
   using(var m=Lock(game))try {
    Guard(game);if(Pack.Native!=null)Pack.Native.Inspect(GameRoot(game));var s=InspectMenu(game);
    if(s.State=="installed"){VerifyArchive(Target(game),s.HasBackup?s.Backup:null);NativeApply(game,true);Progress(100,"Актуальный перевод уже установлен.");return s.Hash;}
    if(!s.CanInstall)throw new InvalidDataException(s.Message);
    string target=Target(game),backup=BackupPath(game);
    long needed=new FileInfo(target).Length*(s.HasBackup?1:2)+64*1024*1024;
    if(new DriveInfo(Path.GetPathRoot(target)).AvailableFreeSpace<needed)throw new IOException("Недостаточно места для оригинала и временного архива. Освободите 3 ГБ.");
    Progress(12,"Сохраняем исходный UI.zip…");EnsureBackup(target,backup);
    string stage=Path.Combine(Path.GetDirectoryName(target),".lmu-ru-"+Guid.NewGuid().ToString("N")+".tmp");
    try {
     var edits=Pack.Info.files.ToDictionary(f=>f.entry,StringComparer.Ordinal);var used=new HashSet<string>();
     using(var input=File.OpenRead(backup))using(var original=new ZipArchive(input,ZipArchiveMode.Read))
     using(var output=new FileStream(stage,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))using(var zip=new ZipArchive(output,ZipArchiveMode.Create)) {
      int i=0;
      foreach(var e in original.Entries) {
       string n=e.FullName.Replace('\\','/');var dst=zip.CreateEntry(e.FullName,CompressionLevel.Optimal);dst.LastWriteTime=e.LastWriteTime;
       using(var b=dst.Open()) {
        PatchFile f;
        if(edits.TryGetValue(n,out f)) {
         if(f.action!="modified")throw new InvalidDataException("Неверная операция ресурса.");
         byte[] before=Package.Read(e);
         if(Package.Hash(before)!=f.before_sha256)throw new InvalidDataException("Исходный ресурс изменён: "+n);
         byte[] bytes=Pack.Transform(f,before);
         if(Package.Hash(bytes)!=f.after_sha256)throw new InvalidDataException("Ошибка преобразования: "+n);
         b.Write(bytes,0,bytes.Length);used.Add(n);
        }else using(var a=e.Open())a.CopyTo(b,1024*1024);
       }
       if(++i%80==0)Progress(15+i*55/Pack.Info.original_entries,"Собираем русский интерфейс… "+i+" / "+Pack.Info.original_entries);
      }
      foreach(var f in Pack.Info.files.Where(f=>f.action=="added")) {
       byte[] bytes=Pack.Transform(f,null);
       if(Package.Hash(bytes)!=f.after_sha256)throw new InvalidDataException("Неверный дополнительный ресурс.");
       var e=zip.CreateEntry(f.entry,CompressionLevel.Optimal);e.LastWriteTime=new DateTimeOffset(2026,9,20,0,0,0,TimeSpan.Zero);
       using(var b=e.Open())b.Write(bytes,0,bytes.Length);used.Add(f.entry);
      }
     }
     if(used.Count!=edits.Count)throw new InvalidDataException("Применены не все изменения.");
     VerifyArchive(stage,backup);string hash=Package.HashFile(stage);
     // Receipt is written before atomic replacement so a crash after commit remains recoverable.
     AtomicText(ReceiptPath(game),Package.Json.Serialize(new Receipt{version=Pack.Info.version,sha256=hash,original_sha256=Pack.Info.original_sha256}));
     Progress(96,"Сохраняем проверенный архив…");Commit(game,stage,s.Hash,hash);NativeApply(game,true);Progress(100,"Русский перевод установлен.");return hash;
    }finally{if(File.Exists(stage))File.Delete(stage);}
   }finally{m.ReleaseMutex();}
  }
  public string Restore(string game) {
   using(var m=Lock(game))try {
    Guard(game);if(Pack.Native!=null)Pack.Native.Inspect(GameRoot(game));var s=InspectMenu(game);if(s.State=="original"){NativeApply(game,false);Progress(100,"Оригинал уже установлен.");return s.Hash;}
    if(!s.CanRestore)throw new InvalidDataException(s.HasBackup?s.Message:"Не найдена проверенная оригинальная копия UI.zip.");
    string stage=Path.Combine(Path.GetDirectoryName(Target(game)),".lmu-ru-restore-"+Guid.NewGuid().ToString("N")+".tmp");
    try{Progress(40,"Восстанавливаем исходный UI.zip…");File.Copy(s.Backup,stage);NativeApply(game,false);Commit(game,stage,s.Hash,Pack.Info.original_sha256);Progress(100,"Оригинальный интерфейс восстановлен. Копия сохранена.");return Pack.Info.original_sha256;}
    finally{if(File.Exists(stage))File.Delete(stage);}
   }finally{m.ReleaseMutex();}
  }
  public static string DetectGame() {
   var roots=new List<string>();
   foreach(var pair in new[]{new[]{@"HKEY_CURRENT_USER\Software\Valve\Steam","SteamPath"},new[]{@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam","InstallPath"}}) {
    var v=Registry.GetValue(pair[0],pair[1],null) as string;if(!string.IsNullOrEmpty(v))roots.Add(v);
   }
   roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam"));
   foreach(var root in roots.ToArray()) {
    string file=Path.Combine(root,"steamapps","libraryfolders.vdf");
    if(File.Exists(file))try{foreach(Match m in Regex.Matches(File.ReadAllText(file),"\"path\"\\s*\"([^\"]+)\""))roots.Add(m.Groups[1].Value.Replace(@"\\",@"\"));}catch(IOException){}
   }
   foreach(string root in roots.Distinct(StringComparer.OrdinalIgnoreCase)) {
    string path=Path.Combine(root,"steamapps","common","Le Mans Ultimate");
    if(File.Exists(Path.Combine(path,"Le Mans Ultimate.exe"))&&File.Exists(Path.Combine(path,"Bin","UI.zip")))return Path.GetFullPath(path);
   }
   return "";
  }
 }
}
