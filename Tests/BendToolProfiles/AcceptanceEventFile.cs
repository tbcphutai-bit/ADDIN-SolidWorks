using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CheckChan.NativeAcceptance
{
    // One STA writer and out-of-process readers coordinate only their log IO.
    // No retry, exception suppression or product/native lifetime change.
    internal static class AcceptanceEventFile
    {
        internal static void Publish(string path,string text)
        {
            WithGate(path,()=>File.WriteAllText(path,text,new UTF8Encoding(false)));
        }
        internal static string Read(string path)
        {
            string text=null;WithGate(path,()=>text=File.ReadAllText(path));return text;
        }
        private static string GateName(string path)
        {
            using(var hash=System.Security.Cryptography.SHA256.Create())
                return "Local\\ADDIN_CheckChan_Event_"+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()))).Replace("-","");
        }
        private static void WithGate(string path,Action action)
        {
            using(var gate=new Mutex(false,GateName(path)))
            {
                // Log coordination only; never hold this across a COM call.
                // Failure remains a failed diagnostic, without an IO retry.
                if(!gate.WaitOne(5000))throw new TimeoutException("Acceptance log IO owner did not release its gate");
                try{action();}finally{gate.ReleaseMutex();}
            }
        }
        internal static int Tests()
        {
            string root=Path.Combine(Path.GetTempPath(),"ADDIN-AcceptanceEvents-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            string path=Path.Combine(root,"events.json");int assertions=0;
            Action<bool> check=ok=>{assertions++;if(!ok)throw new InvalidOperationException("Atomic acceptance event regression");};
            try
            {
                Publish(path,"old");
                Task pending=null;
                using(var started=new ManualResetEventSlim())
                {
                    WithGate(path,()=>{
                        pending=Task.Run(()=>{started.Set();Publish(path,"new");});started.Wait();
                        check(!pending.IsCompleted);check(Read(path)=="old");
                    });
                    pending.GetAwaiter().GetResult();check(Read(path)=="new");
                }
                var json=new JavaScriptSerializer();Publish(path,json.Serialize(new {Version=0,Payload=new string('x',0)}));
                var writer=Task.Run(()=>{for(int n=1;n<=250;n++)Publish(path,new JavaScriptSerializer().Serialize(new {Version=n,Payload=new string('x',n)}));});
                int reads=0;
                do
                {
                    var data=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(Read(path));
                    check(Convert.ToInt32(data["Version"])==Convert.ToString(data["Payload"]).Length);reads++;
                }while(!writer.IsCompleted||reads<250);
                writer.GetAwaiter().GetResult();
                check(Convert.ToInt32(json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(Read(path))["Version"])==250);
                Console.WriteLine("PASS coordinated acceptance file: exclusion while held; 250 publications; "+reads+" complete reads; "+assertions+" assertions");return 0;
            }
            catch(Exception ex){Console.WriteLine("FAIL "+ex);return 1;}
            finally
            {
                // Exact temporary files only. The directory has no CAD input.
                foreach(string file in Directory.GetFiles(root))File.Delete(file);
                Directory.Delete(root,false);
            }
        }
    }
}
