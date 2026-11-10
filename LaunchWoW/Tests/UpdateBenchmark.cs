using System;using System.Collections.Generic;using System.Diagnostics;using System.IO;using System.Linq;
using System.Net;using System.Net.Sockets;using System.Text;using System.Threading;
namespace AzerothCoreLauncher {class UpdateBenchmark {
static int active,maxActive;static volatile bool done,corrupt;static string source;
static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
static void Serve(object state){using(var client=(TcpClient)state)using(var stream=client.GetStream()){
try{var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);string line=reader.ReadLine();if(line==null)return;
string path=Uri.UnescapeDataString(line.Split(' ')[1]);while(!String.IsNullOrEmpty(reader.ReadLine())){}
byte[] body;if(path.EndsWith("manifest.json"))body=Encoding.UTF8.GetBytes(JsonTools.Serialize(ManifestTools.Build(source,null)));
else{int n=Interlocked.Increment(ref active);int previous;do{previous=maxActive;if(n<=previous)break;}while(Interlocked.CompareExchange(ref maxActive,n,previous)!=previous);
try{Thread.Sleep(30);body=File.ReadAllBytes(Path.Combine(source,path.Substring("/client/files/".Length).Replace('/',Path.DirectorySeparatorChar)));if(corrupt&&path.EndsWith("protected.lua"))body[0]^=1;}
finally{Interlocked.Decrement(ref active);}}
byte[] head=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\n\r\n");stream.Write(head,0,head.Length);stream.Write(body,0,body.Length);
}catch(Exception ex){if(!done)Console.Error.WriteLine("SERVER: "+ex.Message);}}}
static int Main(string[] args){string root=Path.GetFullPath(args[0]);source=Path.Combine(root,"source");string target=Path.Combine(root,"target");
foreach(string folder in new[]{source,target}){Directory.CreateDirectory(Path.Combine(folder,"Data","enUS"));Directory.CreateDirectory(Path.Combine(folder,"Interface","AddOns","Fixture"));File.Copy(args[1],Path.Combine(folder,"Wow.exe"),true);File.WriteAllText(Path.Combine(folder,"Data","common.MPQ"),"synthetic common");File.WriteAllText(Path.Combine(folder,"Data","enUS","locale.MPQ"),"synthetic locale");File.WriteAllText(Path.Combine(folder,"Interface","AddOns","Fixture","protected.lua"),"original");}
Directory.CreateDirectory(Path.Combine(target,"WTF"));File.WriteAllText(Path.Combine(target,"WTF","Config.wtf"),"keep settings");File.WriteAllText(Path.Combine(target,"extra.txt"),"keep extra");
for(int i=0;i<180;i++)File.WriteAllText(Path.Combine(source,"Interface","AddOns","Fixture","file"+i.ToString("D3")+".lua"),"-- synthetic addon "+i);
File.WriteAllBytes(Path.Combine(source,"Interface","AddOns","Fixture","empty.lua"),new byte[0]);
DateTime timestamp=DateTime.UtcNow.AddDays(-1);File.SetLastWriteTimeUtc(Path.Combine(target,"Data","common.MPQ"),timestamp);
var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var server=new Uri("http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/client/");
var host=new Thread(delegate(){try{while(!done)ThreadPool.QueueUserWorkItem(Serve,listener.AcceptTcpClient());}catch(SocketException){if(!done)throw;}});host.IsBackground=true;host.Start();
try{var percentages=new List<int>();var timer=Stopwatch.StartNew();
#if FIXED
ClientDownload.Update(server,"",target,null,delegate(int percent,string message){percentages.Add(percent);});
#else
ClientDownload.Update(server,"",target,null);
#endif
long elapsed=timer.ElapsedMilliseconds;
for(int i=0;i<180;i++)Check(File.ReadAllText(Path.Combine(target,"Interface","AddOns","Fixture","file"+i.ToString("D3")+".lua"))=="-- synthetic addon "+i,"addon mismatch");
Check(File.Exists(Path.Combine(target,"Interface","AddOns","Fixture","empty.lua")),"empty file missing");
Check(File.ReadAllText(Path.Combine(target,"WTF","Config.wtf"))=="keep settings"&&File.ReadAllText(Path.Combine(target,"extra.txt"))=="keep extra","settings changed");Check(File.GetLastWriteTimeUtc(Path.Combine(target,"Data","common.MPQ"))==timestamp,"same file replaced");
#if FIXED
Check(percentages[0]==0&&percentages.Last()==100,"progress endpoints");for(int i=1;i<percentages.Count;i++)Check(percentages[i]>=percentages[i-1],"progress regressed");Check(percentages.Take(percentages.Count-1).All(p=>p<100),"100 before verification");Check(maxActive>1&&maxActive<=4,"parallel request limit");Console.WriteLine("PHASE_PROGRESS_OK");
#endif
Console.WriteLine("UPDATE_MANY_SMALL_FILES_OK elapsed_ms="+elapsed+" max_parallel="+maxActive);
File.WriteAllText(Path.Combine(source,"Interface","AddOns","Fixture","protected.lua"),"modified");corrupt=true;bool failed=false;try{ClientDownload.Update(server,"",target,null);}catch(InvalidDataException){failed=true;}catch(IOException){failed=true;}
Check(failed,"corruption accepted");Check(File.ReadAllText(Path.Combine(target,"Interface","AddOns","Fixture","protected.lua"))=="original","original overwritten by corrupt download");
#if FIXED
Check(!Directory.GetFiles(target,"*.azerotbatzoki-update-*",SearchOption.AllDirectories).Any(),"staging left after error");
#endif
Console.WriteLine("CORRUPT_UPDATE_PRESERVES_ORIGINAL_OK");return 0;
}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{done=true;listener.Stop();host.Join(2000);}
}}}
