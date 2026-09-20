using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LmuRu {
 static class Theme {
  public static readonly Color Ink=Color.FromArgb(20,28,33),Paper=Color.FromArgb(244,243,239),Muted=Color.FromArgb(97,107,110),Line=Color.FromArgb(224,226,220),Accent=Color.FromArgb(222,64,39),Green=Color.FromArgb(29,117,90);
  public static GraphicsPath Round(Rectangle r,int radius) {var p=new GraphicsPath();int d=radius*2;p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
 }
 sealed class Card:Panel {
  public int Radius=14;
  public Card(){DoubleBuffered=true;BackColor=Color.White;}
  protected override void OnPaintBackground(PaintEventArgs e) {
   e.Graphics.Clear(Parent==null?Theme.Paper:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
   using(var p=Theme.Round(new Rectangle(0,0,Width-1,Height-1),Radius))using(var b=new SolidBrush(BackColor))using(var pen=new Pen(Theme.Line)){e.Graphics.FillPath(b,p);if(BackColor==Color.White)e.Graphics.DrawPath(pen,p);}
  }
 }
 sealed class RaceButton:Button {
  public bool Primary,Nav,Selected;bool hover;
  public RaceButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;Font=new Font("Segoe UI",10,FontStyle.Bold);}
  protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
  protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
  protected override void OnPaint(PaintEventArgs e) {
   var g=e.Graphics;g.Clear(Parent.BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
   Color bg=Nav?(Selected?Color.FromArgb(44,55,60):hover?Color.FromArgb(34,44,49):Parent.BackColor):!Enabled?Theme.Line:Primary?(hover?Color.FromArgb(194,48,29):Theme.Accent):hover?Color.FromArgb(43,56,63):Theme.Ink;
   using(var p=Theme.Round(new Rectangle(0,0,Width-1,Height-1),9))using(var b=new SolidBrush(bg))g.FillPath(b,p);
   if(Nav&&Selected)using(var b=new SolidBrush(Theme.Accent))g.FillRectangle(b,0,12,3,Height-24);
   var fg=!Enabled?Theme.Muted:Nav&&!Selected?Color.FromArgb(167,179,184):Color.White;
   TextRenderer.DrawText(g,Text,Font,new Rectangle(Nav?17:8,0,Width-(Nav?25:16),Height),fg,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|(Nav?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter));
   if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,new Rectangle(5,5,Width-11,Height-11),Color.White,bg);
  }
 }
 sealed class Hero:Panel {
  public Hero(){DoubleBuffered=true;BackColor=Theme.Ink;}
  protected override void OnPaint(PaintEventArgs e) {
   base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
   using(var grid=new Pen(Color.FromArgb(32,46,52)))for(int x=412;x<Width;x+=26)g.DrawLine(grid,x,0,x-100,Height);
   var points=new[]{new PointF(0,46),new PointF(28,19),new PointF(72,8),new PointF(142,32),new PointF(178,22),new PointF(231,68),new PointF(294,87),new PointF(323,118),new PointF(265,135),new PointF(179,105),new PointF(144,86),new PointF(100,88),new PointF(54,58),new PointF(0,46)};
   var saved=g.Save();g.TranslateTransform(440,8);g.ScaleTransform(.94f,.94f);
   using(var glow=new Pen(Color.FromArgb(55,222,64,39),15))using(var pen=new Pen(Color.FromArgb(238,112,78),3)){glow.LineJoin=LineJoin.Round;pen.LineJoin=LineJoin.Round;g.DrawLines(glow,points);g.DrawLines(pen,points);}
   using(var b=new SolidBrush(Color.White))g.FillEllipse(b,45,50,9,9);g.Restore(saved);
   using(var b=new SolidBrush(Theme.Accent))g.FillRectangle(b,0,0,5,Height);
  }
 }
 public sealed class Launcher:Form {
  public const string Repository="https://github.com/kasyanovea17-crypto/le-mans-ultimate-ru";
  readonly Package pack;readonly Engine engine;readonly bool preview;
  readonly Dictionary<string,Panel> pages=new Dictionary<string,Panel>();readonly Dictionary<string,RaceButton> nav=new Dictionary<string,RaceButton>();
  readonly StringBuilder log=new StringBuilder();readonly ToolTip tips=new ToolTip();
  TextBox path,logView;Label title,subtitle,statusTitle,statusText,backupText,footer,versionLabel;RaceButton install,check,restore,adopt,browse,launch;ProgressBar progress;
  bool busy,closing;Inspection inspection;string verifiedPath="";string current="home";
  readonly string prefs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Karsvein","LMU-RU","game-path.txt");
  [DllImport("user32.dll")]static extern bool ReleaseCapture();
  [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
  public Launcher(Package package,bool previewMode) {
   pack=package;preview=previewMode;engine=new Engine(pack);
   Text="Le Mans Ultimate · Русский перевод";ClientSize=new Size(1080,720);FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.CenterScreen;
   AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;BackColor=Theme.Paper;ForeColor=Theme.Ink;Font=new Font("Segoe UI",10);DoubleBuffered=true;
   var sidebar=new Panel{Bounds=new Rectangle(0,0,198,720),BackColor=Theme.Ink};Controls.Add(sidebar);
   LabelAt(sidebar,"LMU",26,35,150,52,30,FontStyle.Bold,Color.White);
   LabelAt(sidebar,"РУССКИЙ ПЕРЕВОД",29,93,163,23,8,FontStyle.Bold,Color.FromArgb(239,116,83));
   LabelAt(sidebar,"ENDURANCE / RU",29,148,160,20,8,FontStyle.Regular,Color.FromArgb(126,148,154));
   Nav(sidebar,"home","01   Установка",213);Nav(sidebar,"restore","02   Восстановление",266);Nav(sidebar,"help","03   Как пользоваться",319);Nav(sidebar,"log","04   Журнал",372);Nav(sidebar,"about","05   О проекте",425);Nav(sidebar,"updates","06   Обновления",478);
   LabelAt(sidebar,"COMMUNITY EDITION",28,607,169,22,8,FontStyle.Bold,Color.FromArgb(126,148,154));
   LabelAt(sidebar,"KARSVEIN",28,634,160,27,12,FontStyle.Bold,Color.White);LabelAt(sidebar,"Лаунчер 1.1.0",28,673,156,22,8,FontStyle.Regular,Color.FromArgb(159,172,178));
   title=LabelAt(this,"Подготовка к старту",230,28,700,42,24,FontStyle.Bold,Theme.Ink);
   subtitle=LabelAt(this,"Le Mans Ultimate  /  перевод интерфейса",232,74,730,24,10,FontStyle.Regular,Theme.Muted);
   var min=ButtonAt(this,"—",980,29,34,29,false);min.AccessibleName="Свернуть";min.Click+=(s,e)=>WindowState=FormWindowState.Minimized;
   var close=ButtonAt(this,"×",1020,29,34,29,false);close.AccessibleName="Закрыть";close.Click+=(s,e)=>Close();
   foreach(Control c in new Control[]{this,title,subtitle,sidebar})c.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);}};
   var location=new Card{Bounds=new Rectangle(230,110,818,72)};Controls.Add(location);
   LabelAt(location,"ПАПКА ИГРЫ",17,9,400,18,8,FontStyle.Bold,Theme.Muted);
   path=new TextBox{Bounds=new Rectangle(17,34,667,25),BorderStyle=BorderStyle.None,BackColor=Color.White,ForeColor=Theme.Ink,Font=new Font("Segoe UI",10),AccessibleName="Папка Le Mans Ultimate"};location.Controls.Add(path);
   browse=ButtonAt(location,"Выбрать",700,20,100,34,false);browse.Click+=(s,e)=>{using(var d=new FolderBrowserDialog{Description="Выберите папку с Le Mans Ultimate.exe",SelectedPath=path.Text})if(d.ShowDialog(this)==DialogResult.OK)path.Text=d.SelectedPath;};
   path.Text=preview?@"D:\SteamLibrary\steamapps\common\Le Mans Ultimate":LoadPath();path.TextChanged+=(s,e)=>{inspection=null;verifiedPath="";SetStatus("Нужна проверка","Проверьте файлы в выбранной папке.");RefreshButtons();};
   BuildHome();BuildRestore();BuildHelp();BuildLog();BuildAbout();BuildUpdates();
   launch=ButtonAt(this,"Запустить Le Mans Ultimate  →",230,653,306,44,false);launch.AccessibleName="Запустить игру через Steam";launch.Click+=(s,e)=>LaunchGame();
   footer=LabelAt(this,"Выберите папку и проверьте совместимость.",557,648,491,44,9,FontStyle.Regular,Theme.Muted);
   progress=new ProgressBar{Bounds=new Rectangle(558,699,490,3),Minimum=0,Maximum=100,Visible=false};Controls.Add(progress);
   engine.Progress=(n,message)=>{if(!closing&&IsHandleCreated)BeginInvoke((Action)(()=>{progress.Value=Math.Max(0,Math.Min(100,n));footer.Text=message;}));};
   ShowPage("home");SetStatus("Начнём с проверки","Проверим версию игры, словари и резервную копию.");RefreshButtons();
   FormClosing+=(s,e)=>{if(busy){e.Cancel=true;footer.Text="Дождитесь завершения операции с архивом.";}else closing=true;};
   Shown+=async (s,e)=>{nav["home"].Focus();if(preview){SetPreview();return;}if(!string.IsNullOrEmpty(path.Text))await Execute("check",null);};
  }
  string LoadPath(){try{if(File.Exists(prefs)){string value=File.ReadAllText(prefs).Trim();if(File.Exists(Path.Combine(value,"Le Mans Ultimate.exe")))return value;}}catch(IOException){}catch(UnauthorizedAccessException){}return Engine.DetectGame();}
  void SavePath(string value){try{Directory.CreateDirectory(Path.GetDirectoryName(prefs));File.WriteAllText(prefs,value,Encoding.UTF8);}catch(IOException){}catch(UnauthorizedAccessException){}}
  internal static Label LabelAt(Control parent,string text,int x,int y,int w,int h,float size,FontStyle style,Color color) {
   var l=new Label{Text=text,Bounds=new Rectangle(x,y,w,h),Font=new Font("Segoe UI",size,style),ForeColor=color,BackColor=Color.Transparent,AutoSize=false,UseMnemonic=false};parent.Controls.Add(l);return l;
  }
  internal static RaceButton ButtonAt(Control p,string text,int x,int y,int w,int h,bool primary) {
   var b=new RaceButton{Text=text,AccessibleName=text,Bounds=new Rectangle(x,y,w,h),Primary=primary};p.Controls.Add(b);return b;
  }
  void Nav(Control p,string id,string text,int y){var b=ButtonAt(p,text,16,y,166,43,false);b.Nav=true;b.Font=new Font("Segoe UI",9,FontStyle.Bold);nav[id]=b;b.Click+=(s,e)=>ShowPage(id);}
  Panel Page(string id){var p=new Panel{Bounds=new Rectangle(230,204,818,414),BackColor=Theme.Paper};Controls.Add(p);pages[id]=p;return p;}
  void BuildHome() {
   var p=Page("home");var hero=new Hero{Bounds=new Rectangle(0,0,818,150)};p.Controls.Add(hero);
   LabelAt(hero,"НЕОФИЦИАЛЬНЫЙ ПЕРЕВОД",24,18,386,23,8,FontStyle.Bold,Color.FromArgb(236,153,126));
   LabelAt(hero,"Ле-Ман.\nНа твоём языке.",23,46,407,91,24,FontStyle.Bold,Color.White);
   LabelAt(hero,"24H  /  RACE TOGETHER",582,112,220,24,8,FontStyle.Bold,Color.FromArgb(169,186,192));
   var main=new Card{Bounds=new Rectangle(0,168,506,246)};p.Controls.Add(main);
   LabelAt(main,"Знакомая игра. Русский текст.",21,20,465,33,17,FontStyle.Bold,Theme.Ink);
   LabelAt(main,"Меню, HUD и подсказки — с гоночной\nтерминологией и поддержкой кириллицы.",22,66,462,50,11,FontStyle.Regular,Theme.Muted);
   LabelAt(main,"Перед установкой закройте игру. Язык — English.",22,127,466,36,9,FontStyle.Regular,Theme.Muted);
   check=ButtonAt(main,"Проверить файлы",22,184,207,42,false);check.Click+=async(s,e)=>await Execute("check",null);
   install=ButtonAt(main,"Установить перевод",243,184,240,42,true);install.Click+=async(s,e)=>await Execute("install",null);
   var status=new Card{Bounds=new Rectangle(524,168,294,246)};p.Controls.Add(status);
   versionLabel=LabelAt(status,"1.1.0  /  ИГРА 1.4150",19,19,260,21,8,FontStyle.Bold,Theme.Muted);
   statusTitle=LabelAt(status,"",19,54,260,34,15,FontStyle.Bold,Theme.Ink);
   statusText=LabelAt(status,"",19,98,255,66,9,FontStyle.Regular,Theme.Muted);
   LabelAt(status,(pack.Info.reviewed_strings+pack.Native.Info.translated_strings).ToString("N0")+" строк обработано",19,178,258,25,12,FontStyle.Bold,Theme.Ink);
   LabelAt(status,"Меню + HUD + кириллица",19,211,264,22,9,FontStyle.Regular,Theme.Muted);
  }
  void BuildRestore() {
   var p=Page("restore");var c=new Card{Bounds=new Rectangle(0,0,818,414)};p.Controls.Add(c);
   LabelAt(c,"Вернуться к оригиналу",26,24,742,43,23,FontStyle.Bold,Theme.Ink);
   LabelAt(c,"Лаунчер восстанавливает UI.zip, словарь HUD и гоночные шрифты.\nСохранения, профиль пилота и настройки управления остаются на месте.",28,88,752,62,12,FontStyle.Regular,Theme.Muted);
   backupText=LabelAt(c,"Проверенная копия пока не найдена.",28,172,749,77,10,FontStyle.Regular,Theme.Muted);
   restore=ButtonAt(c,"Восстановить оригинал",28,278,309,45,true);restore.Click+=async(s,e)=>{if(MessageBox.Show(this,"Вернуть оригинальный интерфейс Le Mans Ultimate?\nРезервная копия будет сохранена.","Восстановление",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes)await Execute("restore",null);};
   adopt=ButtonAt(c,"Указать исходный UI.zip",355,278,307,45,false);adopt.Click+=async(s,e)=>{using(var d=new OpenFileDialog{Title="Исходный UI.zip из этой версии игры",Filter="Оригинальный UI.zip|UI.zip;UI.original.zip|ZIP-архивы|*.zip",CheckFileExists=true})if(d.ShowDialog(this)==DialogResult.OK)await Execute("adopt",d.FileName);};
   LabelAt(c,"Уже пользовались пробным переводом? Укажите сохранённый оригинал.\nЛаунчер проверит его SHA-256 перед копированием.",28,350,756,49,10,FontStyle.Regular,Theme.Muted);
  }
  void BuildHelp() {
   var p=Page("help");var c=new Card{Bounds=new Rectangle(0,0,818,414)};p.Controls.Add(c);
   LabelAt(c,"От установки до первого старта",25,24,768,44,23,FontStyle.Bold,Theme.Ink);
   string[] names={"01  Выберите игру","02  Установите перевод","03  Оставьте English","04  После обновления Steam"};
   string[] body={"Папка определяется через Steam. Если путь другой — нажмите «Выбрать».","Закройте игру, проверьте файлы и нажмите «Установить перевод».","Русский текст занимает английский языковой слот. Затем запустите игру.","Повторите проверку. Для изменившихся ресурсов потребуется новая версия перевода."};
   for(int i=0;i<4;i++){LabelAt(c,names[i],27,87+i*75,759,28,12,FontStyle.Bold,Theme.Ink);LabelAt(c,body[i],28,117+i*75,756,42,10,FontStyle.Regular,Theme.Muted);}
  }
  void BuildLog() {
   var p=Page("log");var c=new Card{Bounds=new Rectangle(0,0,818,414)};p.Controls.Add(c);
   LabelAt(c,"Журнал операций",24,20,754,42,23,FontStyle.Bold,Theme.Ink);
   logView=new TextBox{Bounds=new Rectangle(26,78,766,250),Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,BackColor=Color.White,Font=new Font("Consolas",10),AccessibleName="Журнал операций"};c.Controls.Add(logView);
   var save=ButtonAt(c,"Сохранить журнал",26,351,235,39,false);save.Click+=(s,e)=>{using(var d=new SaveFileDialog{Filter="Текстовый журнал|*.txt",FileName="LMU-RU-log.txt"})if(d.ShowDialog(this)==DialogResult.OK)try{File.WriteAllText(d.FileName,log.ToString(),Encoding.UTF8);}catch(Exception ex){MessageBox.Show(this,ex.Message,"Журнал");}};
   LabelAt(c,"Журнал хранится локально и не отправляется автоматически.",280,360,512,41,9,FontStyle.Regular,Theme.Muted);
  }
  RaceButton updateCheck,updateDownload; Label updateStatus; UpdateOffer offer;
  void BuildUpdates() {
   var p=Page("updates");var c=new Card{Bounds=new Rectangle(0,0,818,414)};p.Controls.Add(c);
   LabelAt(c,"Перевод, шрифты и HUD",26,24,765,44,23,FontStyle.Bold,Theme.Ink);
   LabelAt(c,"Всё обновляется одним пакетом из нашего GitHub.",28,83,750,28,12,FontStyle.Bold,Theme.Accent);
   LabelAt(c,"1. Проверьте наличие новой версии.\n2. Скачайте проверенный пакет и откройте новый лаунчер.\n3. Закройте игру и нажмите «Обновить перевод».\n\nЗагрузка не меняет файлы игры. Оригинальная копия сохраняется.\nПроверка запускается только по кнопке; учётная запись не нужна.",28,128,750,142,11,FontStyle.Regular,Theme.Muted);
   updateStatus=LabelAt(c,"Установленная версия лаунчера: "+pack.Info.version,28,282,750,54,10,FontStyle.Regular,Theme.Ink);
   updateCheck=ButtonAt(c,"Проверить обновления",28,351,300,42,false);updateCheck.Click+=async(s,e)=>await CheckUpdates();
   updateDownload=ButtonAt(c,"Скачать и открыть",347,351,300,42,true);updateDownload.Enabled=false;updateDownload.Click+=async(s,e)=>await DownloadUpdate();
  }
  async Task CheckUpdates() {
   if(busy||preview)return;busy=true;offer=null;RefreshButtons();updateStatus.Text="Проверяем опубликованные версии…";
   try {offer=await Task.Run(()=>Updates.Check(pack.Info.version));updateStatus.Text=offer==null?"У вас актуальная версия перевода, шрифтов и HUD.":"Доступна версия "+offer.Version+" · "+(offer.Size/1048576.0).ToString("0.0")+" МБ. Проверим SHA-256 после загрузки.";Log("UPDATE_CHECK_PASS · "+(offer==null?"актуально":offer.Version));}
   catch(Exception ex){updateStatus.Text="Проверка не завершена: "+ex.Message;Log("UPDATE_CHECK_FAIL · "+ex.Message);}
   finally{busy=false;RefreshButtons();}
  }
  async Task DownloadUpdate() {
   if(busy||preview||offer==null)return;busy=true;RefreshButtons();updateStatus.Text="Загружаем и проверяем пакет…";
   try {string exe=await Task.Run(()=>Updates.Download(offer,Path.Combine(Path.GetDirectoryName(prefs),"updates")));updateStatus.Text="Пакет проверен. Версия "+offer.Version+" готова к запуску.";Log("UPDATE_DOWNLOAD_PASS · "+offer.Version);
    if(MessageBox.Show(this,"Открыть новый лаунчер "+offer.Version+"?\nВ нём нажмите «Обновить перевод»: обновятся меню, шрифты и HUD.","Обновление готово",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes){Process.Start(new ProcessStartInfo(exe){UseShellExecute=true});busy=false;Close();}
   }catch(Exception ex){updateStatus.Text="Загрузка прервана: "+ex.Message;Log("UPDATE_DOWNLOAD_FAIL · "+ex.Message);}
   finally{busy=false;if(!closing)RefreshButtons();}
  }
  void BuildAbout() {
   var p=Page("about");var c=new Card{Bounds=new Rectangle(0,0,818,414)};p.Controls.Add(c);
   LabelAt(c,"Для тех, кто живёт гонками.",26,24,761,45,23,FontStyle.Bold,Theme.Ink);
   LabelAt(c,"Le Mans Ultimate · Русское сообщество · Karsvein",28,85,753,32,12,FontStyle.Bold,Theme.Accent);
   LabelAt(c,"Меню: 2 393 строки. HUD, боксы и гоночные сообщения: 522 строки.\nКириллица добавлена в гоночные шрифты; исходные символы сохранены.\n\nЧасть подписей встроена в движок или изображения и остаётся английской.\nОригинальный архив игры не распространяется.\n\nНеофициальный проект, не связанный со Studio 397 и Motorsport Games.",28,137,753,181,11,FontStyle.Regular,Theme.Muted);
   var repo=ButtonAt(c,"Исходники на GitHub  ↗",28,351,290,42,false);repo.Click+=(s,e)=>OpenUrl(Repository);
   var updates=ButtonAt(c,"Свежие версии  ↗",336,351,271,42,true);updates.Click+=(s,e)=>OpenUrl(Repository+"/releases");
  }
  internal void ShowPage(string id) {current=id;foreach(var p in pages)p.Value.Visible=p.Key==id;foreach(var n in nav){n.Value.Selected=n.Key==id;n.Value.Invalidate();}title.Text=id=="home"?"Подготовка к старту":id=="restore"?"Возвращение в боксы":id=="help"?"Коротко о главном":id=="log"?"Всё под контролем":id=="updates"?"Всегда на актуальной версии":"Создано для сообщества";if(logView!=null)logView.Text=log.ToString();}
  void SetStatus(string heading,string message){if(statusTitle!=null){statusTitle.Text=heading;statusText.Text=message;}}
  void SetPreview(){inspection=new Inspection{State="installed",HasBackup=true,CanRestore=true,CanLaunch=true,Message="Русский перевод установлен. Оригинал сохранён.",Backup=@"D:\SteamLibrary\steamapps\common\Le Mans Ultimate\Bin\LMU-RU-backup\UI.original.zip"};verifiedPath=path.Text;ApplyInspection();Log("CHECK_PASS · словари и шрифты проверены");footer.Text="Всё готово. Увидимся на трассе.";}
  void ApplyInspection() {
   SetStatus(inspection.State=="installed"?"Готово к старту":inspection.State=="original"?"Можно установить":inspection.State=="previous"?"Есть новая редакция":"Нужна совместимость",inspection.Message);
   statusTitle.ForeColor=inspection.State=="installed"?Theme.Green:Theme.Ink;
   backupText.Text=inspection.HasBackup?"Оригинал проверен и сохранён:\n"+inspection.Backup:"Оригинальная копия пока не сохранена. При установке с исходной игры\nона создаётся автоматически. Для пробной версии укажите её вручную.";
   RefreshButtons();
  }
  void RefreshButtons(){bool valid=inspection!=null&&verifiedPath==path.Text;path.ReadOnly=busy;browse.Enabled=!busy;check.Enabled=!busy;install.Enabled=!busy&&valid&&inspection.CanInstall;install.Text=valid&&inspection.State=="installed"?"Перевод установлен":valid&&inspection.State=="previous"?"Обновить перевод":"Установить перевод";restore.Enabled=!busy&&valid&&inspection.CanRestore;adopt.Enabled=!busy&&valid&&!inspection.HasBackup&&inspection.State!="unknown";if(updateCheck!=null)updateCheck.Enabled=!busy;if(updateDownload!=null)updateDownload.Enabled=!busy&&offer!=null;if(launch!=null)launch.Enabled=!busy&&valid&&inspection.CanLaunch;}
  void Log(string message){log.AppendLine(DateTime.Now.ToString("HH:mm:ss")+"  "+message);if(logView!=null)logView.Text=log.ToString();}
  async Task Execute(string operation,string original) {
   if(busy||preview)return;bool elevate=false;busy=true;RefreshButtons();progress.Visible=true;progress.Value=0;string selected=path.Text;
   Log(operation.ToUpperInvariant()+" · "+selected);
   try {
    await Task.Run(()=>RunOperation(engine,operation,selected,original));
    inspection=await Task.Run(()=>engine.Inspect(selected));verifiedPath=selected;SavePath(selected);ApplyInspection();Log(operation.ToUpperInvariant()+"_PASS · SHA256 "+inspection.Hash);footer.Text=operation=="restore"?"Оригинал восстановлен. Перевод можно установить снова.":operation=="install"?"Перевод установлен. Язык игры — English.":"Проверка завершена. "+(inspection.State=="installed"?"Можно запускать игру.":"Файлы распознаны.");
   }catch(UnauthorizedAccessException) {
    Log("Нужны права записи в папку игры.");
    elevate=operation!="check"&&MessageBox.Show(this,"Windows защищает папку игры.\nРазрешить установщику выполнить эту операцию с правами администратора?","Доступ к папке игры",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes;
    if(!elevate){SetStatus("Нужен доступ к папке","Операция не выполнена. Можно повторить её позднее.");footer.Text="Установка не завершена: нет прав записи.";}
   }catch(Exception ex){inspection=null;verifiedPath="";SetStatus("Проверим ещё раз",ex.Message);footer.Text=ex.Message;Log(operation.ToUpperInvariant()+"_FAIL · "+ex.Message);}
   finally{busy=false;progress.Visible=false;RefreshButtons();}
   if(elevate)await Elevate(operation,selected,original);
  }
  async Task Elevate(string operation,string selected,string original) {
   busy=true;RefreshButtons();progress.Visible=true;
   try {
    Directory.CreateDirectory(Path.GetDirectoryName(prefs));string result=Path.Combine(Path.GetDirectoryName(prefs),"operation-"+Guid.NewGuid().ToString("N")+".json");
    string args="--cli "+operation+" "+Quote(selected)+" "+Quote(result)+(original==null?"":" "+Quote(original));
    var start=new ProcessStartInfo(Application.ExecutablePath,args){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};
    using(var child=Process.Start(start)){await Task.Run(()=>child.WaitForExit());if(child.ExitCode!=0)throw new IOException(File.Exists(result)?File.ReadAllText(result):"Операция не завершена.");}
    inspection=await Task.Run(()=>engine.Inspect(selected));verifiedPath=selected;ApplyInspection();SavePath(selected);Log(operation.ToUpperInvariant()+"_PASS · операция установщика завершена");footer.Text="Готово. "+inspection.Message;
   }catch(Exception ex){inspection=null;verifiedPath="";SetStatus("Операция прервана",ex.Message);Log(ex.Message);footer.Text=ex.Message;}
   finally{busy=false;progress.Visible=false;RefreshButtons();}
  }
  internal static void RunOperation(Engine engine,string operation,string game,string original) {switch(operation){case "check":engine.Verify(game);break;case "install":engine.Install(game);break;case "restore":engine.Restore(game);break;case "adopt":engine.AdoptOriginal(game,original);break;default:throw new ArgumentException("Неизвестная операция.");}}
  static string Quote(string v){if(v==null||v.Contains("\""))throw new ArgumentException("Недопустимый путь.");return "\""+v.TrimEnd('\\')+"\"";}
  void LaunchGame(){if(busy||inspection==null||verifiedPath!=path.Text)return;try{Engine.GameRoot(path.Text);if(Engine.GameRunning()){footer.Text="Игра уже запущена.";return;}if(MessageBox.Show(this,"Запустить Le Mans Ultimate через Steam?\nЯзык игры для перевода: English.","На старт",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes){Process.Start(new ProcessStartInfo("steam://rungameid/2399420"){UseShellExecute=true});Log("LAUNCH · запрос запуска передан Steam");footer.Text="Запрос запуска передан Steam.";}}catch(Exception ex){Log(ex.Message);MessageBox.Show(this,ex.Message,"Запуск игры");}}
  void OpenUrl(string url){if(preview)return;try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(this,ex.Message,"Открыть ссылку");}}
  internal int UiAssertions() {
   int n=0;Action<bool> assert=b=>{if(!b)throw new InvalidDataException("UI assertion failed #"+n);n++;};
   assert(pages.Count==6);assert(nav.Count==6);assert(!install.Enabled);assert(!restore.Enabled);assert(!launch.Enabled);
   SetPreview();assert(launch.Enabled);assert(restore.Enabled);assert(!install.Enabled);assert(!adopt.Enabled);
   foreach(string id in pages.Keys){ShowPage(id);assert(current==id);assert(nav[id].Selected);}
   ShowPage("home");busy=true;RefreshButtons();assert(!check.Enabled&&!install.Enabled&&!restore.Enabled&&!launch.Enabled&&!browse.Enabled);busy=false;
   path.Text=@"D:\Changed";assert(inspection==null);assert(!install.Enabled&&!restore.Enabled&&!launch.Enabled);SetPreview();
   assert(Repository.StartsWith("https://github.com/"));assert(Engine.DetectGame()!=null);assert(pack.Info.reviewed_strings==2393);
   return n;
  }
  internal void SavePreview(string file,string page){SetPreview();ShowPage(page);using(var b=new Bitmap(Width,Height)){DrawToBitmap(b,new Rectangle(0,0,Width,Height));b.Save(file,System.Drawing.Imaging.ImageFormat.Png);}}
 }
 static class Program {
  [STAThread] public static int Main(string[] args) {
   try {
    var pack=Package.Load();
    if(args.Length>=4&&args[0]=="--cli") {
     try{var engine=new Engine(pack);Launcher.RunOperation(engine,args[1],args[2],args.Length>4?args[4]:null);var s=engine.Inspect(args[2]);File.WriteAllText(args[3],Package.Json.Serialize(new{result=args[1].ToUpperInvariant()+"_PASS",state=s.State,sha256=s.Hash,backup=s.HasBackup}),Encoding.UTF8);return 0;}
     catch(Exception ex){File.WriteAllText(args[3],Package.Json.Serialize(new{result=args[1].ToUpperInvariant()+"_FAIL",message=ex.Message}),Encoding.UTF8);return 1;}
    }
    Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
    if(args.Length>=2&&args[0]=="--self-test") {using(var f=new Launcher(pack,true)){f.CreateControl();int n=f.UiAssertions();File.WriteAllText(args[1],"UI_TEST_PASS checks="+n+" real_game_writes=false\n",Encoding.UTF8);}return 0;}
    if(args.Length>=2&&args[0]=="--preview") {using(var f=new Launcher(pack,true)){f.Show();Application.DoEvents();f.SavePreview(args[1],args.Length>2?args[2]:"home");}return 0;}
    Application.Run(new Launcher(pack,false));return 0;
   }catch(Exception ex){if(args.Length>=2&&args[0].StartsWith("--")){File.WriteAllText(args[1]+".error.txt",ex.ToString());}else MessageBox.Show(ex.Message,"Le Mans Ultimate RU",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
  }
 }
}
