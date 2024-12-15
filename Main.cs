using libdebug;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PS4Saves
{
    public partial class Main : Form
    {
        PS4DBG ps4 = new PS4DBG();
        private int pid;
        private ulong stub;
        private ulong libSceUserServiceBase = 0x0;
        private ulong libSceSaveDataBase = 0x0;
        private int user = 0x0;
        string mp = "";
        bool log = false;

        public Main()
        {
            InitializeComponent();
            fwVersionComboBox.Items.AddRange(Offsets.Firmwares);
            // fwVersionComboBox.SelectedItem = Offsets.SelectedFirmware;
            fwVersionComboBox.SelectedValueChanged += (sender, e) =>
            {
                Offsets.SelectedFirmware = fwVersionComboBox.SelectedItem.ToString();
            };

            string[] args = Environment.GetCommandLineArgs();
            if (args.Length == 2 && args[1] == "-log")
            {
                log = true;
            }

            if (File.Exists("ip"))
            {
                ipTextBox.Text = File.ReadAllText("ip");
            }
        }
        public static string FormatSize(double size)
        {
            const long BytesInKilobytes = 1024;
            const long BytesInMegabytes = BytesInKilobytes * 1024;
            const long BytesInGigabytes = BytesInMegabytes * 1024;
            double value;
            string str;
            if (size < BytesInGigabytes)
            {
                value = size / BytesInMegabytes;
                str = "MB";
            }
            else
            {
                value = size / BytesInGigabytes;
                str = "GB";
            }
            return String.Format("{0:0.##} {1}", value, str);
        }
        private void sizeTrackBar_Scroll(object sender, EventArgs e)
        {
            sizeToolTip.SetToolTip(sizeTrackBar, FormatSize((double)(sizeTrackBar.Value * 32768)));
        }
        private void SetStatus(string msg)
        {
            statusLabel.Text = $"Status: {msg}";
            this.Refresh();
        }
        private void WriteLog(string msg)
        {
            if (log)
            {

                msg = $"|{msg}|";
                var a = msg.Length / 2;
                for (var i = 0; i < 48 - a; i++)
                {
                    msg = msg.Insert(0, " ");
                }

                for (var i = msg.Length; i < 96; i++)
                {
                    msg += " ";
                }

                var dateAndTime = DateTime.Now;
                var logStr = $"|{dateAndTime:MM/dd/yyyy} {dateAndTime:hh:mm:ss tt}| |{msg}|";

                if (File.Exists(@"log.txt"))
                {
                    File.AppendAllText(@"log.txt",
                        $"{logStr}{Environment.NewLine}");
                }
                else
                {
                    using (var sw = File.CreateText(@"log.txt"))
                    {
                        sw.WriteLine(logStr);
                    }
                }

                Console.WriteLine(logStr);
            }
        }
        private void connectButton_Click(object sender, EventArgs e)
        {
            try
            {
                ps4 = new PS4DBG(ipTextBox.Text);
                ps4.Connect();
                if (!ps4.IsConnected)
                {
                    throw new Exception();
                }
                SetStatus("Connected");
                if (!File.Exists("ip"))
                {
                    File.WriteAllText("ip", ipTextBox.Text);
                }
                else
                {
                    using (var sw = File.CreateText(@"log.txt"))
                    {
                        sw.Write(ipTextBox.Text);
                    }
                }
            }
            catch
            {
                SetStatus("Failed To Connect");
            }
        }

        private Process[] filter(ProcessList list)
        {
            List<Process> procs = new List<Process>();
            for (int i = 0; i < list.processes.Length; i++)
            {
                if (list.processes[i].name == "eboot.bin" || list.processes[i].name.EndsWith(".elf"))
                {
                    procs.Add(list.processes[i]);
                }
            }
            return procs.ToArray();
        }

        private void processesButton_Click(object sender, EventArgs e)
        {
            if (!ps4.IsConnected)
            {
                SetStatus("Not Connected");
                return;
            }
            processesComboBox.DataSource = filter(ps4.GetProcessList());
            SetStatus("Refreshed Processes");
        }

        private void setupButton_Click(object sender, EventArgs e)
        {
            if (pid == 0)
            {
                SetStatus("No Process Selected");
                return;
            }
            var pm = ps4.GetProcessMaps(pid);
            var tmp = pm.FindEntry("libSceSaveData.sprx")?.start;
            if (tmp == null)
            {
                MessageBox.Show("savedata lib not found", "Error");
                return;
            }
            libSceSaveDataBase = (ulong)tmp;

            tmp = pm.FindEntry("libSceUserService.sprx")?.start;
            if (tmp == null)
            {
                MessageBox.Show("user service lib not found", "Error");
                return;
            }
            libSceUserServiceBase = (ulong)tmp;

            stub = ps4.InstallRPC(pid); // dummy in ps5debug

            var ids = GetLoginList();
            List<User> users = new List<User>();
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == -1)
                {
                    continue;
                }
                users.Add(new User { id = ids[i], name = GetUserName(ids[i]) });
            }
            userComboBox.DataSource = users.ToArray();

            var ret = ps4.Call(pid, stub, libSceSaveDataBase + Offsets.sceSaveDataInitialize3);
            WriteLog($"sceSaveDataInitialize3 ret = 0x{ret:X}");
            
            //PATCHES
            bool found_sce_str = false;
            bool found_sce_sdmemory_str = false;
            bool found_underscore_check = false;

            // https://github.com/ChendoChap/Playstation-4-Save-Mounter/blob/e792dfacb3f9f787a61c5daf948f8a615aac7c7c/Main.cs#L219

            // '_' patch pattern
            // 80 E2 DF 80 C2 BF 80 FA 1A 72 ?? // ps4 - uses dl
            // 80 E3 DF 80 C3 BF 80 FB 1A 72 ?? // ps5 - uses bl

            // 'sce_' patch pattern
            // 00 73 63 65 5F 00

            // 'sce_sdmemory' patch pattern
            // 00 73 63 65 5F 73 64 6D 65 6D 6F 72 79 00

            SetStatus("Patching libSceSaveData... (this may take a while)");
            foreach (var entry in pm.entries.Where(x => x.name.Contains("libSceSaveData")))
            {
                var data = ps4.ReadMemory(pid, entry.start, (int)(entry.end - entry.start));

                for (int i = 0; i < data.Length; i++)
                {
                    if (i + 10 < data.Length &&
                        data[i] == 0x80 &&
                        (data[i + 1] == 0xE2 || data[i + 1] == 0xE3) &&
                        data[i + 2] == 0xDF &&
                        data[i + 3] == 0x80 &&
                        (data[i + 4] == 0xC2 || data[i + 4] == 0xC3) &&
                        data[i + 5] == 0xBF &&
                        data[i + 6] == 0x80 &&
                        (data[i + 7] == 0xFA || data[i + 7] == 0xFB) &&
                        data[i + 9] == 0x72)
                    {
                        if (data[i + 8] == 0x1A)
                        {
                            found_underscore_check = true;
                            WriteLog($"Found underscore check at 0x{entry.start + (ulong)i:X}");
                            // replace the 1A with 0x30, at offset 8
                            ps4.WriteMemory(pid, entry.start + (ulong)i + 8, (byte)0x30);
                            WriteLog($"Patched underscore check at 0x{entry.start + (ulong)i:X}");
                        }
                        else if (data[i + 8] == 0x30)
                        {
                            found_underscore_check = true;
                            WriteLog($"Underscore check already patched at 0x{entry.start + (ulong)i:X}");
                        }
                    }
                    else if (i + 5 < data.Length &&
                            data[i] == 0x00 &&
                            data[i + 2] == 0x63 &&
                            data[i + 3] == 0x65 &&
                            data[i + 4] == 0x5F &&
                            data[i + 5] == 0x00)
                    {
                        if (data[i + 1] == 0x73)
                        {
                            found_sce_str = true;
                            WriteLog($"Found sce_ string at 0x{entry.start + (ulong)i:X}");
                            ps4.WriteMemory(pid, entry.start + (ulong)i + 1, (byte)0x00);
                            WriteLog($"Patched sce_ string at 0x{entry.start + (ulong)i:X}");
                        }
                        else if (data[i + 1] == 0x00)
                        {
                            found_sce_str = true;
                            WriteLog($"sce_ string already patched at 0x{entry.start + (ulong)i:X}");
                        }
                    }
                    else if (i + 13 < data.Length &&
                            data[i] == 0x00 &&
                            data[i + 2] == 0x63 &&
                            data[i + 3] == 0x65 &&
                            data[i + 4] == 0x5F &&
                            data[i + 5] == 0x73 &&
                            data[i + 6] == 0x64 &&
                            data[i + 7] == 0x6D &&
                            data[i + 8] == 0x65 &&
                            data[i + 9] == 0x6D &&
                            data[i + 10] == 0x6F &&
                            data[i + 11] == 0x72 &&
                            data[i + 12] == 0x79 &&
                            data[i + 13] == 0x00)
                    {
                        if (data[i + 1] == 0x73)
                        {
                            found_sce_sdmemory_str = true;
                            WriteLog($"Found sce_sdmemory string at 0x{entry.start + (ulong)i:X}");
                            ps4.WriteMemory(pid, entry.start + (ulong)i + 1, (byte)0x00);
                            WriteLog($"Patched sce_sdmemory string at 0x{entry.start + (ulong)i:X}");
                        }
                        else if (data[i + 1] == 0x00)
                        {
                            found_sce_sdmemory_str = true;
                            WriteLog($"sce_sdmemory string already patched at 0x{entry.start + (ulong)i:X}");
                        }
                    }
                }
            }

            SetStatus("Patching SceShellCore... (this may take a while)");
            bool found_sce_sdmemory_str_in_shellcore = false;
            {
                // SceShellCore patches
                // 'sce_sdmemory' patch
                // TODO: verify keystone patch

                var procs = ps4.GetProcessList();
                var shellcore = procs.FindProcess("SceShellCore");
                if (shellcore == null)
                {
                    MessageBox.Show("SceShellCore not found", "Error");
                    return;
                }

                var shellcore_pm = ps4.GetProcessMaps(shellcore.pid);

                foreach (var entry in shellcore_pm.entries.Where(x => x.name == "executable"))
                {
                    var data = ps4.ReadMemory(shellcore.pid, entry.start, (int)(entry.end - entry.start));

                    for (int i = 0; i < data.Length; i++)
                    {
                        if (i + 14 < data.Length &&
                            data[i] == 0x00 &&
                            data[i + 2] == 0x63 &&
                            data[i + 3] == 0x65 &&
                            data[i + 4] == 0x5F &&
                            data[i + 5] == 0x73 &&
                            data[i + 6] == 0x64 &&
                            data[i + 7] == 0x6D &&
                            data[i + 8] == 0x65 &&
                            data[i + 9] == 0x6D &&
                            data[i + 10] == 0x6F &&
                            data[i + 11] == 0x72 &&
                            data[i + 12] == 0x79 &&
                            data[i + 13] == 0x00)
                        {
                            if (data[i + 1] == 0x73)
                            {
                                found_sce_sdmemory_str_in_shellcore = true;
                                WriteLog($"Found sce_sdmemory string at 0x{entry.start + (ulong)i:X}");
                                ps4.WriteMemory(shellcore.pid, entry.start + (ulong)i + 1, (byte)0x00);
                                WriteLog($"Patched sce_sdmemory string at 0x{entry.start + (ulong)i:X}");
                            }
                            else if (data[i + 1] == 0x00)
                            {
                                found_sce_sdmemory_str_in_shellcore = true;
                                WriteLog($"sce_sdmemory string already patched at 0x{entry.start + (ulong)i:X}");
                            }
                        }
                    }

                }

            }

            if (!found_sce_str || !found_sce_sdmemory_str || !found_underscore_check)
            {
                MessageBox.Show("Wasnt able to apply all ingame sce_ patches, some saves may not show up.", "Warning");
            }

            if (!found_sce_sdmemory_str_in_shellcore)
            {
                MessageBox.Show("Wasnt able to apply all SceShellCore sce_sdmemory patches, some saves may not show up.", "Warning");
            }


            SetStatus("Setup Done :)");
        }

        private void searchButton_Click(object sender, EventArgs e)
        {
            if (pid == 0)
            {
                SetStatus("No Process Selected");
                return;
            }
            var pm = ps4.GetProcessMaps(pid);

            var dirNameAddr = ps4.AllocateMemory(pid, Marshal.SizeOf(typeof(SceSaveDataDirName)) * 1024);
            var paramAddr = ps4.AllocateMemory(pid, Marshal.SizeOf(typeof(SceSaveDataParam)) * 1024);
            SceSaveDataDirNameSearchCond searchCond = new SceSaveDataDirNameSearchCond
            {
                userId = GetUser()
            };
            SceSaveDataDirNameSearchResult searchResult = new SceSaveDataDirNameSearchResult
            {
                dirNames = dirNameAddr,
                dirNamesNum = 1024,
                param = paramAddr,
            };
            dirsComboBox.DataSource = Find(searchCond, searchResult);
            ps4.FreeMemory(pid, dirNameAddr, Marshal.SizeOf(typeof(SceSaveDataDirName)) * 1024);
            ps4.FreeMemory(pid, paramAddr, Marshal.SizeOf(typeof(SceSaveDataParam)) * 1024);
            if (dirsComboBox.Items.Count > 0)
            {
                SetStatus($"Found {dirsComboBox.Items.Count} Save Directories :D");
            }
            else
            {
                SetStatus("Found 0 Save Directories :(");
            }
        }

        private void mountButton_Click(object sender, EventArgs e)
        {
            if (dirsComboBox.Items.Count == 0)
            {
                return;
            }
            var dirNameAddr = ps4.AllocateMemory(pid, Marshal.SizeOf(typeof(SceSaveDataDirName)));
            SceSaveDataDirName dirName = new SceSaveDataDirName
            {
                data = dirsComboBox.Text
            };

            SceSaveDataMount2 mount = new SceSaveDataMount2
            {
                userId = GetUser(),
                dirName = dirNameAddr,
                blocks = 32768,
                mountMode = 0x8 | 0x2,

            };
            SceSaveDataMountResult mountResult = new SceSaveDataMountResult
            {

            };
            ps4.WriteMemory(pid, dirNameAddr, dirName);
            mp = Mount(mount, mountResult);

            ps4.FreeMemory(pid, dirNameAddr, Marshal.SizeOf(typeof(SceSaveDataDirName)));
            if (mp != "")
            {
                SetStatus($"Save Mounted in {mp}");
            }
            else
            {
                SetStatus("Mounting Failed");
            }
        }

        private void unmountButton_Click(object sender, EventArgs e)
        {
            if (mp == "")
            {
                SetStatus("No save mounted");
                return;
            }
            SceSaveDataMountPoint mountPoint = new SceSaveDataMountPoint
            {
                data = mp,
            };

            Unmount(mountPoint);
            mp = null;
            SetStatus("Save Unmounted");
        }

        private void createButton_Click(object sender, EventArgs e)
        {
            if (pid == 0)
            {
                SetStatus("No Process Selected");
                return;
            }
            var pm = ps4.GetProcessMaps(pid);

            if (nameTextBox.Text == "")
            {
                SetStatus("No Save Name");
                return;
            }
            var dirNameAddr = ps4.AllocateMemory(pid, Marshal.SizeOf(typeof(SceSaveDataDirName)));
            SceSaveDataDirName dirName = new SceSaveDataDirName
            {
                data = nameTextBox.Text
            };

            SceSaveDataMount2 mount = new SceSaveDataMount2
            {
                userId = GetUser(),
                dirName = dirNameAddr,
                blocks = (ulong)sizeTrackBar.Value,
                mountMode = 4 | 2 | 8,

            };
            SceSaveDataMountResult mountResult = new SceSaveDataMountResult
            {

            };
            ps4.WriteMemory(pid, dirNameAddr, dirName);
            var mp = Mount(mount, mountResult);
            ps4.FreeMemory(pid, dirNameAddr, Marshal.SizeOf(typeof(SceSaveDataDirName)));
            if (mp != "")
            {
                SetStatus("Save Created");
                SceSaveDataMountPoint mountPoint = new SceSaveDataMountPoint
                {
                    data = mp,
                };
                Unmount(mountPoint);
            }
            else
            {
                SetStatus("Save Creation Failed");
            }
        }

        private int GetUser()
        {
            if (user != 0)
            {
                return user;
            }
            else
            {
                return InitialUser();
            }
        }

        private int InitialUser()
        {
            var bufferAddr = ps4.AllocateMemory(pid, sizeof(int));

            ps4.Call(pid, stub, libSceUserServiceBase + Offsets.sceUserServiceGetInitialUser, bufferAddr);

            var id = ps4.ReadMemory<int>(pid, bufferAddr);

            ps4.FreeMemory(pid, bufferAddr, sizeof(int));

            return id;
        }

        private int[] GetLoginList()
        {
            var bufferAddr = ps4.AllocateMemory(pid, sizeof(int) * 4);
            ps4.Call(pid, stub, libSceUserServiceBase + Offsets.sceUserServiceGetLoginUserIdList, bufferAddr);

            var id = ps4.ReadMemory(pid, bufferAddr, sizeof(int) * 4);
            var size = id.Length / sizeof(int);
            var ints = new int[size];
            for (var index = 0; index < size; index++)
            {
                ints[index] = BitConverter.ToInt32(id, index * sizeof(int));
            }
            ps4.FreeMemory(pid, bufferAddr, sizeof(int));

            return ints;
        }

        private string GetUserName(int userid)
        {
            var bufferAddr = ps4.AllocateMemory(pid, 17);
            ps4.Call(pid, stub, libSceUserServiceBase + Offsets.sceUserServiceGetUserName, userid, bufferAddr, 17);
            var str = ps4.ReadMemory<string>(pid, bufferAddr);
            ps4.FreeMemory(pid, bufferAddr, 17);
            return str;
        }

        private SearchEntry[] Find(SceSaveDataDirNameSearchCond searchCond, SceSaveDataDirNameSearchResult searchResult)
        {
            var searchCondAddr = ps4.AllocateMemory(pid, Marshal.SizeOf(typeof(SceSaveDataDirNameSearchCond)));
            var searchResultAddr = ps4.AllocateMemory(pid, Marshal.SizeOf(typeof(SceSaveDataDirNameSearchResult)));

            ps4.WriteMemory(pid, searchCondAddr, searchCond);
            ps4.WriteMemory(pid, searchResultAddr, searchResult);
            var ret = ps4.Call(pid, stub, libSceSaveDataBase + Offsets.sceSaveDataDirNameSearch, searchCondAddr, searchResultAddr);
            WriteLog($"sceSaveDataDirNameSearch ret = 0x{ret:X}");
            if (ret == 0)
            {
                searchResult = ps4.ReadMemory<SceSaveDataDirNameSearchResult>(pid, searchResultAddr);
                SearchEntry[] sEntries = new SearchEntry[searchResult.hitNum];
                for (uint i = 0; i < searchResult.hitNum; i++)
                {
                    SceSaveDataParam tmp = ps4.ReadMemory<SceSaveDataParam>(pid, searchResult.param + i * (uint)Marshal.SizeOf(typeof(SceSaveDataParam)));
                    sEntries[i] = new SearchEntry
                    {
                        dirName = ps4.ReadMemory<string>(pid, searchResult.dirNames + i * 32),
                        title = tmp.title,
                        subtitle = tmp.subTitle,
                        detail = tmp.detail,
                        time = new DateTime(1970, 1, 1).ToLocalTime().AddSeconds(tmp.mtime).ToString(),
                    };
                }
                ps4.FreeMemory(pid, searchCondAddr, Marshal.SizeOf(typeof(SceSaveDataDirNameSearchCond)));
                ps4.FreeMemory(pid, searchResultAddr, Marshal.SizeOf(typeof(SceSaveDataDirNameSearchResult)));
                return sEntries;
            }

            ps4.FreeMemory(pid, searchCondAddr, Marshal.SizeOf(typeof(SceSaveDataDirNameSearchCond)));
            ps4.FreeMemory(pid, searchResultAddr, Marshal.SizeOf(typeof(SceSaveDataDirNameSearchResult)));

            return new SearchEntry[0];

        }

        private string Mount(SceSaveDataMount2 mount, SceSaveDataMountResult mountResult)
        {
            var mountAddr = ps4.AllocateMemory(pid, Marshal.SizeOf(typeof(SceSaveDataMount2)));
            var mountResultAddr = ps4.AllocateMemory(pid, Marshal.SizeOf(typeof(SceSaveDataMountResult)));

            ps4.WriteMemory(pid, mountAddr, mount);
            ps4.WriteMemory(pid, mountResultAddr, mountResult);
            var ret = ps4.Call(pid, stub, libSceSaveDataBase + Offsets.sceSaveDataMount2, mountAddr, mountResultAddr);
            WriteLog($"sceSaveDataMount2 ret = 0x{ret:X}");
            if (ret == 0)
            {
                mountResult = ps4.ReadMemory<SceSaveDataMountResult>(pid, mountResultAddr);

                ps4.FreeMemory(pid, mountAddr, Marshal.SizeOf(typeof(SceSaveDataMount2)));
                ps4.FreeMemory(pid, mountResultAddr, Marshal.SizeOf(typeof(SceSaveDataMountResult)));

                return mountResult.mountPoint.data;
            }

            ps4.FreeMemory(pid, mountAddr, Marshal.SizeOf(typeof(SceSaveDataMount2)));
            ps4.FreeMemory(pid, mountResultAddr, Marshal.SizeOf(typeof(SceSaveDataMountResult)));

            return "";
        }

        private void Unmount(SceSaveDataMountPoint mountPoint)
        {
            var mountPointAddr = ps4.AllocateMemory(pid, Marshal.SizeOf(typeof(SceSaveDataMountPoint)));

            ps4.WriteMemory(pid, mountPointAddr, mountPoint);
            var ret = ps4.Call(pid, stub, libSceSaveDataBase + Offsets.sceSaveDataUmount, mountPointAddr);
            WriteLog($"sceSaveDataUmount ret = 0x{ret:X}");
            ps4.FreeMemory(pid, mountPointAddr, Marshal.SizeOf(typeof(SceSaveDataMountPoint)));
            mp = null;
        }

        private string TransferMount(SceSaveDataTransferringMount mount, SceSaveDataMountResult mountResult)
        {
            var mountAddr = ps4.AllocateMemory(pid, Marshal.SizeOf(typeof(SceSaveDataTransferringMount)));
            var mountResultAddr = ps4.AllocateMemory(pid, Marshal.SizeOf(typeof(SceSaveDataMountResult)));

            ps4.WriteMemory(pid, mountAddr, mount);
            ps4.WriteMemory(pid, mountResultAddr, mountResult);
            var ret = ps4.Call(pid, stub, libSceSaveDataBase + Offsets.sceSaveDataTransferringMount, mountAddr, mountResultAddr);
            WriteLog($"sceSaveDataTransferringMount ret = 0x{ret:X}");
            if (ret == 0)
            {
                mountResult = ps4.ReadMemory<SceSaveDataMountResult>(pid, mountResultAddr);

                ps4.FreeMemory(pid, mountAddr, Marshal.SizeOf(typeof(SceSaveDataTransferringMount)));
                ps4.FreeMemory(pid, mountResultAddr, Marshal.SizeOf(typeof(SceSaveDataMountResult)));

                return mountResult.mountPoint.data;
            }

            ps4.FreeMemory(pid, mountAddr, Marshal.SizeOf(typeof(SceSaveDataTransferringMount)));
            ps4.FreeMemory(pid, mountResultAddr, Marshal.SizeOf(typeof(SceSaveDataMountResult)));

            return "";
        }

        class SearchEntry
        {
            public string dirName;
            public string title;
            public string subtitle;
            public string detail;
            public string time;
            public override string ToString()
            {
                return dirName;
            }
        }

        private void dirsComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            titleTextBox.Text = ((SearchEntry)dirsComboBox.SelectedItem).title;
            subtitleTextBox.Text = ((SearchEntry)dirsComboBox.SelectedItem).subtitle;
            detailsTextBox.Text = ((SearchEntry)dirsComboBox.SelectedItem).detail;
            dateTextBox.Text = ((SearchEntry)dirsComboBox.SelectedItem).time;
        }

        private void processesComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            pid = ((Process)processesComboBox.SelectedItem).pid;
        }

        private void userComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            user = ((User)userComboBox.SelectedItem).id;
        }

        class User
        {
            public int id;
            public string name;

            public override string ToString()
            {
                return name;
            }
        }
    }
}
