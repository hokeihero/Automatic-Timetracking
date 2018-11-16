﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows;
using System.Diagnostics;
using System.Windows.Automation;
using Newtonsoft.Json;
using System.Web.Helpers;
using System.Web.Script.Serialization;
using System.Text.RegularExpressions;
using System.Threading;

namespace WindowsFormsApp2
{
    public partial class Form1 : Form
    {
        //windows event hook initializations---------------------------------------------------
        [DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        //call back pointer
        delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        private const uint WINEVENT_OUTOFCONTEXT = 0;
        private const uint EVENT_SYSTEM_FOREGROUND = 3;             //win title change event
        private const uint EVENT_OBJECT_NAMECHANGE = 0X800C;        //win title change event - can detect tab change
        private const uint EVENT_SYSTEM_CAPTURESTART = 0x0008;

        string prevTitle = string.Empty;
        string prevPs = string.Empty;
        string prevUrl = string.Empty;
        string elapsedTime = string.Empty;
        Stopwatch stopwatch = new Stopwatch();
        TimeSpan ts;

        //using mutex to prevent threads from modifying dictionaryEvents values simultaneously
        Mutex myMutex = new Mutex();                                
        Mutex startPollingMutex = new Mutex();
        Mutex startPostingMutex = new Mutex();


        Dictionary<string, string> winTitle2url = new Dictionary<string, string>();

        int i = 0;

        public Form1()
        //public Form1()
        {
            InitializeComponent();
            label6.Text = Global.name;
            label9.Text = "Choose a project to begin session...";

            //format
            this.TopMost = true;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            this.CenterToScreen();
            listView1.Items[listView1.Items.Count - 1].EnsureVisible();



            //wait until a project is selected
            startPollingMutex.WaitOne();
            startPostingMutex.WaitOne();

            //polling thread
            System.Threading.Thread pollingThread;
            pollingThread = new System.Threading.Thread(startPolling);
            pollingThread.IsBackground = true;
            pollingThread.Start();

            //posting thread
            System.Threading.Thread postThread;
            postThread = new System.Threading.Thread(startPosting);
            postThread.IsBackground = true;
            postThread.Start();

            //dlg = new WinEventDelegate(WinEventProc);
            // IntPtr m_hhook = SetWinEventHook(EVENT_SYSTEM_CAPTURESTART, EVENT_SYSTEM_CAPTURESTART, IntPtr.Zero, dlg, 0, 0, WINEVENT_OUTOFCONTEXT);
        }
        WinEventDelegate dlg = null;    //prevent program from crashing if initialized here

        //triggers on mouse click - reserved
        public void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {

        }

        //thread to poll
        public void startPolling()
        {
            //wait for project selection before starting
            startPollingMutex.WaitOne(-1, false);                                

            //duration
            stopwatch.Start();

            while (true)
            {
                System.Threading.Thread.Sleep(50);
                if (ProcessInfo.getForegroundProcName().Equals("chrome"))
                {
                    try
                    {
                        if (!prevTitle.Equals(ProcessInfo.getForegroundWinTitle())) //tab changed or navigated to different url
                        {
                            ts = stopwatch.Elapsed;
                            
                            //insert event into dictionary (association will be performed in there)
                            dictionaryInsert();
                            
                            stopwatch.Restart();

                            //update title and process
                            prevTitle = ProcessInfo.getForegroundWinTitle();
                            prevPs = ProcessInfo.getForegroundProcName().ToLower();

                            //fetch URL of visited site from dictionary to prevent re-seeks
                            if (winTitle2url.ContainsKey(prevTitle))
                            {
                                label8.Text = "Skipped";
                                prevUrl = winTitle2url[prevTitle];
                            }
                            else
                            {
                                label8.Text = "Ran";

                                Thread urlThread = new Thread(getChromeUrl);
                                urlThread.Start();
                                urlThread.Join();
                                //prevUrl = GetUrl.chrome();

                                prevTitle = ProcessInfo.getForegroundWinTitle();        //updating winTitle after grabbing URL for higher accuracy...
                                                                                        //...due to it not changing at the same as the URL in chrome

                                winTitle2url.Add(prevTitle, prevUrl);                   //stores into table
                            }
                        }

                        //in case URL is modified by regular applications due to fast focus switchings
                        if (prevUrl.Equals(""))
                        {
                            label8.Text = "Oops ";

                            if (winTitle2url.ContainsKey(prevTitle))
                            {
                                prevUrl = winTitle2url[prevTitle];
                                //MessageBox.Show(prevUrl);
                            }
                                
                            else
                            {
                                Thread urlThread = new Thread(getChromeUrl);
                                urlThread.Start();
                                urlThread.Join();
                                //prevUrl = GetUrl.chrome();
                                prevTitle = ProcessInfo.getForegroundWinTitle();        //updating winTitle

                                winTitle2url.Add(prevTitle, prevUrl);                   //stores into table
                            }
                        }
                    }
                    catch
                    {
                        /*
                        in case of extreme fast window switching causing 'prevTitle'
                        to be corrupted during capture and ultimately leading winTitle2url.ContainsKey(prevTitle)
                        to return FALSE, when it supposed to be TRUE if the dictionary already have such
                        an entry (when captured correctly), an exception will be thrown when trying to insert a non-corrupted version
                        of 'prevTitle' into dictionary, which is obtained right before winTitle2url.Add(prevTitle, prevUrl)
                        in line 147
                        */
                        winTitle2url.Remove(prevTitle);                                         //remove corrupted winTitle entry from dictionary
                        continue;
                    }
                    label1.Text = prevTitle;
                    label2.Text = prevPs;
                    label4.Text = prevUrl;
                }//end chrome
 /*non-chrome*/ else                                                                           
                {
                    if (!(string.IsNullOrEmpty(prevTitle) || string.IsNullOrEmpty(prevPs)))     //prevent empty entry being inserted into listview
                    {

                        if (!prevTitle.Equals(ProcessInfo.getForegroundWinTitle()))
                        {
                            ts = stopwatch.Elapsed;

                            //insert event into dictionary (association will be performed in there)
                            dictionaryInsert();
                            
                            stopwatch.Restart();
                        }
                    }
                    prevTitle = ProcessInfo.getForegroundWinTitle();
                    prevPs = ProcessInfo.getForegroundProcName().ToLower();
                    prevUrl = "";

                    label1.Text = prevTitle;
                    label2.Text = prevPs;
                    label4.Text = prevUrl;
                    label8.Text = "";
                }//end non-chrome
            }//end while
        }//end polling thread

        //thread to post
        public void startPosting()
        {
            //wait for project selection before starting
            startPostingMutex.WaitOne(-1, false);                                

            DateTime start = DateTime.Today.AddHours(6.0);      //adds 5 hours for central time
            DateTime end;

            string description = string.Empty;
            string entryId = string.Empty;
            string value = string.Empty;                        //either process name or URL
            string taskId = string.Empty;

            while (true)
            {
                System.Threading.Thread.Sleep(5000);

                //looping through dictionary to post or put depending on if the event has been posted
                myMutex.WaitOne();

                label7.Text = i.ToString();
                i++;

                try
                {
                    foreach (var x in Global.dictionaryEvents)
                    {
                        //undefined events (events with empty task ID) will not be uploaded
                        if (x.Value.taskId.Equals(""))
                            continue;

                        if (x.Value.entryId.Equals(""))                         //POST, empty ID means this event hasn't been posted
                        {
                            if (x.Key.process.Equals("chrome"))
                            {
                                description = x.Key.url;
                                value = x.Key.url;
                                taskId = x.Value.taskId;
                            }

                            else
                            {
                                description = x.Key.process;
                                value = x.Key.process;
                                taskId = x.Value.taskId;
                            }


                            end = DateTime.Parse(x.Value.ts.ToString()).AddHours(6.0);

                            dynamic res = API.AddTimeEntry(start, end, description, Global.workspaceId, Global.projectId, taskId);

                            Global.dictionaryEvents[x.Key].entryId = res.id;    //update dictionary value to include entry ID returned from clockify
                        }
                        else                                                    //PUT   
                        {
                            if (x.Key.process.Equals("chrome"))
                            {
                                description = x.Key.url;
                                value = x.Key.url;
                                taskId = x.Value.taskId;
                            }
                            else
                            {
                                description = x.Key.process;
                                value = x.Key.process;
                                taskId = x.Value.taskId;
                            }

                            entryId = x.Value.entryId;
                            end = DateTime.Parse(x.Value.ts.ToString()).AddHours(6.0);

                            API.UpdateTimeEntry(start, end, description, entryId, Global.workspaceId, Global.projectId, taskId);
                        }
                    }//end foreach
                }
                catch (Exception ex)
                {
                    MessageBox.Show("POSTING - " + ex.ToString());
                }
                myMutex.ReleaseMutex();

            }//end while
        }//end posting thread

        //associate event to task ID and names for 'dictionaryEvent'
        public List<dynamic> associateForDictionaryEvents()
        {
            Event e = new Event();
            EventValues idt = new EventValues();
            List<dynamic> associatedSet = new List<dynamic>();

            e.winTitle = prevTitle;
            if (prevPs.Equals("chrome"))        //in case of user switching focus too fast between chrome and other applications
                e.url = prevUrl;
            else
                e.url = "";
            e.process = prevPs;

            idt.entryId = "";
            idt.ts = ts;

            //associate task by URL or process name based on if URL is empty
            try
            {
                if (!e.url.Equals(""))                                          //empty, it's a chrome event
                {
                    idt.taskId = Global.associations[prevUrl].id;
                    idt.taskName = Global.associations[prevUrl].name;
                }

                else                                                            //not empty, it's a nonchrome event
                {
                    idt.taskId = Global.associations[prevPs].id;
                    idt.taskName = Global.associations[prevPs].name;
                }
            }
            catch
            {
                idt.taskId = "";
                idt.taskName = "Undefined";
            }

           
            associatedSet.Add(e);
            associatedSet.Add(idt);

            return associatedSet;
        }//end associateDictionary

        //insert events into dictionary
        public void dictionaryInsert()
        {
            myMutex.WaitOne();

            //perform association
            List<dynamic> associatedSet = associateForDictionaryEvents();

            Event e = associatedSet[0];                     //key
            EventValues idt = associatedSet[1];             //value

            try
            {
                if (Global.dictionaryEvents.ContainsKey(e))                                //if an event is already in the table, update timespan
                    Global.dictionaryEvents[e].ts = Global.dictionaryEvents[e].ts + ts;
                else
                {
                    if (!filter(e))
                    {
                        myMutex.ReleaseMutex();
                        return;
                    }
                    Global.dictionaryEvents.Add(e, idt);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }


            //clear and post all onto listview1
            listView1.Items.Clear();
            string elapsedTime;
            listView1.BeginUpdate();
            foreach (var x in Global.dictionaryEvents)
            {
                elapsedTime = String.Format("{0:00}:{1:00}:{2:00}", x.Value.ts.Hours, x.Value.ts.Minutes, x.Value.ts.Seconds);
                ListViewItem lv = new ListViewItem(x.Key.winTitle);

                lv.SubItems.Add(x.Key.url);
                lv.SubItems.Add(x.Key.process);
                lv.SubItems.Add(elapsedTime);
                lv.SubItems.Add(x.Value.taskName);
                listView1.Items.Add(lv);
            }
            listView1.EndUpdate();
            listView1.Items[listView1.Items.Count - 1].EnsureVisible();
            myMutex.ReleaseMutex();
        }//end dictionaryInsert

        //filters url and process names
        public bool filter(Event e)                                           //returns true if entry is good for insert 
        {
            string pattern = @"\.(com|net|edu|org)$";       //dot anything is assumed to be an url

            Match match = Regex.Match(e.winTitle, pattern);

            if (match.Success)                                                  //if winTitle is an url
            {
                //MessageBox.Show(match.Value);
                return false;
            }
            else if (e.process.Equals("chrome"))
            {
                if (e.winTitle.Equals("Untitled - Google Chrome") ||
                    e.winTitle.Equals("New Tab - Google Chrome") ||
                    e.winTitle.Equals("Downloads - Google Chrome") ||
                    e.url.Equals("/") ||
                    e.url.Equals("")
                   )
                {
                    return false;
                }
            }
            else if (e.process.Equals("explorer"))
            {
                if (e.winTitle.Equals("Program Manager") ||
                    e.winTitle.Equals("File Explorer") ||
                    e.winTitle.Equals("")
                    )
                {
                    return false;
                }

            }
            else if (e.process.Equals("idle") ||
                     e.process.Equals("ShellExperienceHost") ||
                    (e.winTitle.Equals("File Explorer") && (e.winTitle.Equals("explorer"))) ||
                     e.winTitle.Equals("")
                )
            {
                return false;
            }

            return true;
        }//end filter

        //start association from scratch, clears out all current dictionaries
        public void associateRaw()
        {
            listView1.Items.Clear();
            string prevTitle = string.Empty;
            string prevPs = string.Empty;
            string prevUrl = string.Empty;

            Global.dictionaryEvents.Clear();
            Global.associations.Clear();

            //binds task ID and name together, must be done before calling 'loadAssociation' since Association object needed to lookup task name by task ID
            bindTaskIdName();                       

            //load and associate value->taskId using SQL
            List<Association> processes = SQL.loadAssociations(1);
            List<Association> URLs = SQL.loadAssociations(2);

            //adds event->task association
            foreach (Association ps in processes)
            {
                Dto.TaskDto t = new Dto.TaskDto() { id = ps.taskId, name = ps.taskName };

                Global.associations.Add(ps.value, t);       
            }

            //adds event->task association
            foreach (Association url in URLs)
            {
                Dto.TaskDto t = new Dto.TaskDto() { id = url.taskId, name = url.taskName };

                Global.associations.Add(url.value, t);      
            }

        }

        //binds task ID and name together
        private void bindTaskIdName()
        {
            Global.taskIdName.Clear();

            List<Dto.TaskDto> tasks = API.getTasksByProjectId(Global.workspaceId, Global.projectId);
            foreach (Dto.TaskDto t in tasks)
            {
                Global.taskIdName.Add(t.id, t.name);
            }
        }

        //associations (Form 4)
        private void button1_Click(object sender, EventArgs e)      
        {
            myMutex.WaitOne();                      //prevent inserting into dictionary while making association changes

            Form4 f = new Form4();
            f.ShowDialog();

            if (Global.chosen == 1 )
            {
                associateRaw();
                Global.chosen = 0;
            }

            myMutex.ReleaseMutex();
        }

        //projects (Form 3)
        private void button2_Click(object sender, EventArgs e)      
        {
            myMutex.WaitOne();                      //prevent inserting into dictionary while making association changes

            Form3 f = new Form3();
            f.ShowDialog();

            if (Global.chosen == 1)
            {
                label9.Text = Global.projectName;
                label13.Text = Global.workspaceName;

                associateRaw();

                try { startPollingMutex.ReleaseMutex(); } catch { }
                try { startPostingMutex.ReleaseMutex(); } catch { }

                Global.chosen = 0;
            }

            myMutex.ReleaseMutex();
        }

        //delete time entries of a workspace
        private void button3_Click(object sender, EventArgs e)
        {
            if (Global.workspaceId.Equals(string.Empty))
            {
                MessageBox.Show("Session is not running, choose a workspace/project first.");
                return;
            }
                

            myMutex.WaitOne();

            //reset environment
            associateRaw();

            List<Dto.TimeEntryFullDto> entries = API.FindTimeEntriesByWorkspace(Global.workspaceId);

            foreach(Dto.TimeEntryFullDto entry in entries)
            {
                API.DeleteTimeEntry(Global.workspaceId, entry.id);
            }

            myMutex.ReleaseMutex();

        }

        //thread to get chrome Url
        public void getChromeUrl()
        {
            prevUrl = GetUrl.chrome();
        }


        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void Form1_Load(object sender, EventArgs e)
        {


        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void label5_Click(object sender, EventArgs e)
        {

        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void label6_Click(object sender, EventArgs e)
        {

        }

        private void label4_Click(object sender, EventArgs e)
        {

        }
        
        private void label7_Click(object sender, EventArgs e)
        {

        }

        private void label13_Click(object sender, EventArgs e)
        {

        }


    }
}
