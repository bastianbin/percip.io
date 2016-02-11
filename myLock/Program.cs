﻿using Appccelerate.CommandLineParser;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Microsoft.Win32.TaskScheduler;
using System.Reflection;

namespace myLock
{
    public enum TriggerType
    {
        Logon,
        Unlock,
        Lock,
        Logoff
    }
    public class Program
    {
        private static string dbFile = Environment.CurrentDirectory + "\\times.db";
        private static string taskPrefix = "__myLock__";

        static void Main(string[] args)
        {
            string direction = string.Empty;
            bool query = false;
            bool raw = false;
            bool init = false;
            bool deInit = false;
            bool help = false;
            string inject = string.Empty;

            var configuration = CommandLineParserConfigurator
                .Create()
                .WithSwitch("q", () => query = true).HavingLongAlias("query").DescribedBy("Call the db and get your working times.")
                .WithSwitch("r", () => raw = true).HavingLongAlias("raw").DescribedBy("Get all logged events")
                .WithSwitch("i", () => init = true).HavingLongAlias("init").DescribedBy("Create windows tasks (you need elevated permissions for this one!")
                .WithSwitch("d", () => deInit = true).HavingLongAlias("deinit").DescribedBy("Remove windows tasks (you need elevated permissions for this one!")
                .WithSwitch("h", () => help = true).HavingLongAlias("help").DescribedBy("Show this usage screen.")
                .WithNamed("j", I => inject = I).HavingLongAlias("inject").DescribedBy("Time|Direction\"", "Use this for debugging only! You can inject timestamps. 1 for lock, 0 for unlock")
                .WithPositional(d => direction = d).DescribedBy("lock", "tell me to \"lock\" for \"out\" and keep empty for \"in\"")
                .BuildConfiguration();
            var parser = new CommandLineParser(configuration);

            var parseResult = parser.Parse(args);

            if (!parseResult.Succeeded)
            {
                Console.WriteLine("wrong arguments");
                Environment.Exit(-1);
            }
            else
            {
                if (help)
                    ShowUsage(configuration);
                if (init)
                {
                    using (TaskService ts = new TaskService())
                    {
                        try
                        {
                            DefineTask(ts, "myLock lock screen", "lock_pc", TriggerType.Lock);
                            DefineTask(ts, "myLock unlock screen", "unlock_pc", TriggerType.Unlock);
                            DefineTask(ts, "myLock login to pc", "login_pc", TriggerType.Logon);
                            DefineTask(ts, "myLock logout from pc", "logout_pc", TriggerType.Logoff);
                            Console.WriteLine("Initialization complete.");
                            Environment.Exit(0);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            Console.Error.WriteLine("Access denied, please use an elevated prompt.");
                            Environment.Exit(-1);
                        }
                    }
                }
                if (deInit)
                {
                    using (TaskService ts = new TaskService())
                    {
                        try
                        {
                            //Remove tasks
                            foreach (var t in ts.RootFolder.AllTasks)
                                if (t.Name.StartsWith(taskPrefix))
                                    ts.RootFolder.DeleteTask(t.Name);

                            Console.WriteLine("Deinitialization complete.");
                            Environment.Exit(0);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            Console.Error.WriteLine("Access denied, please use an elevated prompt.");
                            Environment.Exit(-1);
                        }
                    }
                }
                if (!string.IsNullOrEmpty(inject))
                {
                    TimeStampCollection col;
                    try
                    {
                        col = DecryptAndDeserialize<TimeStampCollection>(dbFile, GetKey());
                    }
                    catch (FileNotFoundException)
                    {
                        col = new TimeStampCollection();
                    }
                    TimeStamp stamp = new TimeStamp();
                    string[] injection = inject.Split('|');
                    stamp.Stamp = DateTime.Parse(injection[0]);
                    if (injection.Length > 1)
                        stamp.Direction = (Direction)(Convert.ToInt32(injection[1]));
                    if (injection.Length > 2)
                        stamp.User = injection[2];

                    col.TimeStamps.Add(stamp);
                    col.TimeStamps.Sort();
                    EncryptAndSerialize<TimeStampCollection>(dbFile, col, GetKey());

                    Console.WriteLine("Injection successfull.");
                    Console.WriteLine("Values were: {0}", inject);
                    Environment.Exit(0);
                }

                if (raw)
                {
                    TimeStampCollection col = DecryptAndDeserialize<TimeStampCollection>(dbFile, GetKey());
                    foreach (var t in col.TimeStamps)
                        Console.WriteLine("{0} {1} {2}", t.Stamp, t.User, t.Direction);

                    Console.WriteLine("EOF");
                    Environment.Exit(0);
                }

                if (!query)
                    LogTimeStamp(direction);
                else
                    QueryWorkingTimes();
            }
        }

        private static void ShowUsage(CommandLineConfiguration configuration)
        {
            Usage usage = new UsageComposer(configuration).Compose();
            Console.WriteLine(@"

 ███▄ ▄███▓▓██   ██▓ ██▓     ▒█████   ▄████▄   ██ ▄█▀
▓██▒▀█▀ ██▒ ▒██  ██▒▓██▒    ▒██▒  ██▒▒██▀ ▀█   ██▄█▒ 
▓██    ▓██░  ▒██ ██░▒██░    ▒██░  ██▒▒▓█    ▄ ▓███▄░ 
▒██    ▒██   ░ ▐██▓░▒██░    ▒██   ██░▒▓▓▄ ▄██▒▓██ █▄ 
▒██▒   ░██▒  ░ ██▒▓░░██████▒░ ████▓▒░▒ ▓███▀ ░▒██▒ █▄
░ ▒░   ░  ░   ██▒▒▒ ░ ▒░▓  ░░ ▒░▒░▒░ ░ ░▒ ▒  ░▒ ▒▒ ▓▒
░  ░      ░ ▓██ ░▒░ ░ ░ ▒  ░  ░ ▒ ▒░   ░  ▒   ░ ░▒ ▒░
░      ░    ▒ ▒ ░░    ░ ░   ░ ░ ░ ▒  ░        ░ ░░ ░ 
       ░    ░ ░         ░  ░    ░ ░  ░ ░      ░  ░   
            ░ ░                      ░               
MyLock - The working time logger by antic_eye ;)

Use this tool to track your productivity. MyLock generates an
encrypted database file that contains timestamps and ""in""
or ""out"".

When you call myLock with ""lock"" it tracks:
01.01.2016T08: 15 Hans.Meiser Out

When you call without args it tracks:
01.01.2016T08: 19 Hans.Meiser In

When you want to show your times, call it with ""-query"".It will
read the db and calculate your working time beginning with the
first ""in"" per day, ending with the last ""out"".

To automate the tracking, use ""-init"" and myLock will generate
Windows Scheduled tasks for screen lock/ unlock and session
login / -out. You will need administrative permissions for this
task. Open an elevated command prompt.

");

            Console.WriteLine("Usage: mylock.exe {0}", usage.Arguments);
            Console.WriteLine();
            Console.WriteLine(usage.Options);
            Environment.Exit(0);
        }

        private static void DefineTask(TaskService ts, string description, string taskName, TriggerType trigger)
        {
            string executable = string.Format("{0}\\mylock.exe", AssemblyDirectory);
            TaskDefinition td = ts.NewTask();
            td.Principal.RunLevel = TaskRunLevel.LUA;
            td.RegistrationInfo.Description = description;

            switch (trigger)
            {
                case TriggerType.Logon:
                    td.Triggers.Add(new LogonTrigger());
                    td.Actions.Add(new ExecAction(executable, "", AssemblyDirectory));
                    break;
                case TriggerType.Unlock:
                    td.Triggers.Add(new SessionStateChangeTrigger(TaskSessionStateChangeType.SessionUnlock));
                    td.Actions.Add(new ExecAction(executable, "", AssemblyDirectory));
                    break;
                case TriggerType.Logoff:
                    td.Triggers.Add(new SessionStateChangeTrigger(TaskSessionStateChangeType.ConsoleDisconnect));
                    td.Actions.Add(new ExecAction(executable, "lock", AssemblyDirectory));
                    break;
                case TriggerType.Lock:
                    td.Triggers.Add(new SessionStateChangeTrigger(TaskSessionStateChangeType.SessionLock));
                    td.Actions.Add(new ExecAction(executable, "lock", AssemblyDirectory));
                    break;
            }
            ts.RootFolder.RegisterTaskDefinition(taskPrefix + taskName, td);
        }

        public static string AssemblyDirectory
        {
            get
            {
                string codeBase = Assembly.GetExecutingAssembly().CodeBase;
                UriBuilder uri = new UriBuilder(codeBase);
                string path = Uri.UnescapeDataString(uri.Path);
                return Path.GetDirectoryName(path);
            }
        }
        private static void QueryWorkingTimes()
        {
#if !DEBUG
            TimeStampCollection col = DecryptAndDeserialize<TimeStampCollection>(dbFile, GetKey());
#else
                    TimeStampCollection col = new TimeStampCollection();
                    col.TimeStamps.Add(new TimeStamp()
                    {
                        Direction = Direction.In,
                        Stamp = new DateTime(2015, 01, 20, 8, 30, 0),
                        User = Environment.UserName
                    });
                    col.TimeStamps.Add(new TimeStamp()
                    {
                        Direction = Direction.Out,
                        Stamp = new DateTime(2015, 01, 20,12, 30, 0),
                        User = Environment.UserName
                    });
                    col.TimeStamps.Add(new TimeStamp()
                    {
                        Direction = Direction.In,
                        Stamp = new DateTime(2015, 01, 20, 12, 45, 0),
                        User = Environment.UserName
                    });
                    col.TimeStamps.Add(new TimeStamp()
                    {
                        Direction = Direction.Out,
                        Stamp = new DateTime(2015, 01, 20, 16, 35, 0),
                        User = Environment.UserName
                    });
                    col.TimeStamps.Add(new TimeStamp()
                    {
                        Direction = Direction.In,
                        Stamp = new DateTime(2015, 01, 21, 8, 00, 0),
                        User = Environment.UserName
                    });
                    col.TimeStamps.Add(new TimeStamp()
                    {
                        Direction = Direction.Out,
                        Stamp = new DateTime(2015, 01, 21, 11, 45, 0),
                        User = Environment.UserName
                    });
                    col.TimeStamps.Add(new TimeStamp()
                    {
                        Direction = Direction.In,
                        Stamp = new DateTime(2015, 01, 21, 12, 30, 0),
                        User = Environment.UserName
                    });
                    col.TimeStamps.Add(new TimeStamp()
                    {
                        Direction = Direction.Out,
                        Stamp = new DateTime(2015, 01, 21, 18, 58, 0),
                        User = Environment.UserName
                    });
                    col.TimeStamps.Add(new TimeStamp()
                    {
                        Direction = Direction.In,
                        Stamp = new DateTime(2015, 01, 22, 8, 30, 0),
                        User = Environment.UserName
                    });
                    col.TimeStamps.Add(new TimeStamp()
                    {
                        Direction = Direction.Out,
                        Stamp = new DateTime(2015, 01, 22, 16, 30, 0),
                        User = Environment.UserName
                    });
#endif
            col.TimeStamps.Sort();
            DateTime dtIn = DateTime.MinValue;
            DateTime dtOut = DateTime.MinValue;
            DateTime currentDay = DateTime.MinValue;

            List<string> days = new List<string>();

            for (int i = 0; i < col.TimeStamps.Count; i++)
            {
                DateTime nextDay = (i < col.TimeStamps.Count - 1) ? col.TimeStamps[i + 1].Stamp.Date : DateTime.Now.Date;

                if (currentDay == DateTime.MinValue || nextDay > currentDay)//1st run
                {
#if DEBUG
                    Console.WriteLine("Today is {0}, the next entry is from {1}; Changing day", currentDay, nextDay);
#endif
                    if (DateTime.MinValue != dtIn && DateTime.MinValue != dtOut && dtIn.Date == dtOut.Date)
                        PrintTime(dtIn, dtOut);

                    currentDay = col.TimeStamps[i].Stamp.Date;
                    if (col.TimeStamps[i].Direction == Direction.In)//first unlock is start of work
                    {
                        dtIn = col.TimeStamps[i].Stamp;
                        dtOut = DateTime.MinValue;
                    }
                }
                if (col.TimeStamps[i].Direction == Direction.Out && (nextDay > currentDay))//lock is end of work
                    dtOut = col.TimeStamps[i].Stamp;
            }
            PrintTime(dtIn, dtOut);
        }

        private static void PrintTime(DateTime dtIn, DateTime dtOut)
        {
            try
            {
                if (dtOut == DateTime.MinValue)
                    Console.WriteLine("{0:yyyy-MM-dd ddd}\t {1:HH\\:mm} in and till now ({2:HH\\:mm}) {3:hh\\:mm} h of work", dtIn.Date, dtIn, DateTime.Now, (DateTime.Now - dtIn));
                else
                    Console.WriteLine("{0:yyyy-MM-dd ddd}\t {1:HH\\:mm} in and {2:HH\\:mm} out. {3:hh\\:mm} h of work", dtIn.Date, dtIn, dtOut, (dtOut - dtIn));
            }
            catch (FormatException ex)
            {
                Console.Error.WriteLine("dtIn {0}", dtIn);
                Console.Error.WriteLine("dtOut {0}", dtOut);
            }
        }

        private static void LogTimeStamp(string direction)
        {
            Direction dir = Direction.In;
            if (direction.Trim().ToLowerInvariant() == "lock")
                dir = Direction.Out;

            TimeStamp stamp = new TimeStamp()
            {
                Direction = dir,
                Stamp = DateTime.Now,
                User = Environment.UserName
            };

            string sMyKey = GetKey();

            TimeStampCollection col = null;
            if (File.Exists(dbFile))
            {
                try
                {
                    col = DecryptAndDeserialize<TimeStampCollection>(dbFile, sMyKey);
                }
                catch (CryptographicException ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    Environment.Exit(2);
                }
            }
            else
                col = new TimeStampCollection();

            col.TimeStamps.Add(stamp);
            EncryptAndSerialize<TimeStampCollection>(dbFile, col, sMyKey);
            Console.WriteLine("Saved: {0}|", stamp.Stamp, stamp.Direction);
        }

        private static string GetKey()
        {
            string sMyKey = Environment.UserName + "@" + Environment.UserDomainName;
            int iBitSize = 16;
            if (sMyKey.Length > iBitSize)
                sMyKey = sMyKey.Substring(0, iBitSize);
            if (sMyKey.Length < iBitSize)
                for (int i = sMyKey.Length; i < iBitSize; i++)
                    sMyKey = "~" + sMyKey;
            return sMyKey;
        }

        public static void EncryptAndSerialize<T>(string filename, T obj, string encryptionKey)
        {
            var key = new DESCryptoServiceProvider();
            int length = encryptionKey.Length / 2;
            byte[] k = Encoding.ASCII.GetBytes(encryptionKey.Substring(0, length));
            byte[] iV = Encoding.ASCII.GetBytes(encryptionKey.Substring(length));
            var e = key.CreateEncryptor(k, iV);
            try
            {
                using (var fs = File.Open(filename, FileMode.Create))
                {
                    using (var cs = new CryptoStream(fs, e, CryptoStreamMode.Write))
                    {
                        (new XmlSerializer(typeof(T))).Serialize(cs, obj);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                Environment.Exit(-1);
            }
        }

        public static T DecryptAndDeserialize<T>(string filename, string encryptionKey)
        {
            var key = new DESCryptoServiceProvider();
            int length = encryptionKey.Length / 2;
            byte[] k = Encoding.ASCII.GetBytes(encryptionKey.Substring(0, length));
            byte[] iV = Encoding.ASCII.GetBytes(encryptionKey.Substring(length));
            var d = key.CreateDecryptor(k, iV);
            try
            {
                using (var fs = File.Open(filename, FileMode.Open))
                using (var cs = new CryptoStream(fs, d, CryptoStreamMode.Read))
                    return (T)(new XmlSerializer(typeof(T))).Deserialize(cs);
            }
            catch (FileNotFoundException)
            {
                Console.Error.WriteLine("{0} could not be found", filename);
                Environment.Exit(-1);
                return default(T);
            }
        }
    }
}
