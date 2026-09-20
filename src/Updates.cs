using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace LmuRu {
 public sealed class ReleaseAsset { public string name,browser_download_url,digest; public long size; }
 public sealed class ReleaseInfo { public string tag_name,html_url; public bool draft,prerelease; public ReleaseAsset[] assets; }
 public sealed class UpdateOffer { public string Version,Url,Hash,Name; public long Size; }
 public static class Updates {
  public const string Api="https://api.github.com/repos/kasyanovea17-crypto/le-mans-ultimate-ru/releases/latest";
  const string Prefix="https://github.com/kasyanovea17-crypto/le-mans-ultimate-ru/releases/download/";
  public static Version ParseVersion(string value) {
   if(value==null||!Regex.IsMatch(value,@"\Av?\d{1,5}\.\d{1,5}\.\d{1,5}\z"))throw new InvalidDataException("Некорректная версия обновления.");
   return new Version(value.TrimStart('v'));
  }
  public static UpdateOffer Parse(string json,string installed) {
   var r=Package.Json.Deserialize<ReleaseInfo>(json);
   if(r==null||r.draft||r.prerelease)throw new InvalidDataException("Релиз ещё не опубликован.");
   var version=ParseVersion(r.tag_name);var current=ParseVersion(installed.Split('-')[0]);
   if(version<=current)return null;
   string name="LMU-RU-Launcher-"+version+"-win-x64.zip";
   var candidates=(r.assets??new ReleaseAsset[0]).Where(a=>a.name==name).ToArray();
   if(candidates.Length!=1)throw new InvalidDataException("В релизе отсутствует однозначный пакет для Windows.");
   var asset=candidates[0];
   if(asset.browser_download_url!=Prefix+r.tag_name+"/"+name||asset.size<1||asset.size>64*1024*1024||asset.digest==null||!Regex.IsMatch(asset.digest,@"\Asha256:[a-f0-9]{64}\z"))throw new InvalidDataException("Адрес, размер или SHA-256 пакета не прошли проверку.");
   return new UpdateOffer{Version=version.ToString(),Url=asset.browser_download_url,Hash=asset.digest.Substring(7),Name=name,Size=asset.size};
  }
  static byte[] Fetch(string url,long maximum) {
   ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
   for(int redirect=0;redirect<5;redirect++) {
    var uri=new Uri(url);
    if(uri.Scheme!="https"||!(uri.Host=="api.github.com"||uri.Host=="github.com"||uri.Host=="release-assets.githubusercontent.com"))throw new InvalidDataException("Неожиданный сервер обновления.");
    var request=(HttpWebRequest)WebRequest.Create(uri);request.UserAgent="LMU-RU-Launcher";request.Timeout=30000;request.ReadWriteTimeout=30000;request.AllowAutoRedirect=false;
    using(var response=(HttpWebResponse)request.GetResponse()) {
     if((int)response.StatusCode>=300&&(int)response.StatusCode<400){url=new Uri(uri,response.Headers["Location"]).AbsoluteUri;continue;}
     if(response.StatusCode!=HttpStatusCode.OK||response.ContentLength>maximum)throw new IOException("Сервер вернул неожиданный ответ.");
     using(var stream=response.GetResponseStream())using(var output=new MemoryStream()) {
      var buffer=new byte[65536];int read;while((read=stream.Read(buffer,0,buffer.Length))>0){if(output.Length+read>maximum)throw new InvalidDataException("Пакет превышает допустимый размер.");output.Write(buffer,0,read);}return output.ToArray();
     }
    }
   }
   throw new IOException("Слишком много перенаправлений.");
  }
  public static UpdateOffer Check(string installed){return Parse(Encoding.UTF8.GetString(Fetch(Api,2*1024*1024)),installed);}
  public static string Download(UpdateOffer offer,string directory){return Unpack(offer,Fetch(offer.Url,offer.Size),directory);}
  public static string Unpack(UpdateOffer offer,byte[] bytes,string directory) {
   if(bytes.LongLength!=offer.Size||Package.Hash(bytes)!=offer.Hash)throw new InvalidDataException("Контрольная сумма загрузки не совпадает. Игра не изменена.");
   string exe=null;
   using(var input=new MemoryStream(bytes))using(var zip=new ZipArchive(input,ZipArchiveMode.Read)) {
    var entries=zip.Entries.Where(e=>e.Name=="LMU-RU-Launcher.exe").ToArray();
    if(entries.Length!=1||entries[0].Length<2||entries[0].Length>64*1024*1024)throw new InvalidDataException("Неоднозначный исполняемый файл обновления.");
    // Extract only the known embedded-payload launcher, never arbitrary archive paths.
    Directory.CreateDirectory(directory);string fresh=Path.Combine(directory,offer.Version+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fresh);
    exe=Path.Combine(fresh,"LMU-RU-Launcher.exe");
    using(var source=entries[0].Open())using(var output=new FileStream(exe,FileMode.CreateNew))source.CopyTo(output);
    using(var f=File.OpenRead(exe))if(f.ReadByte()!=77||f.ReadByte()!=90){File.Delete(exe);throw new InvalidDataException("Не найден Windows-лаунчер.");}
   }
   return exe;
  }
 }
}
