﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using Chocobot.Datatypes;
using Chocobot.MemoryStructures.Character;
using Chocobot.Utilities.Memory;

namespace Chocobot.Utilities.Navigation
{

    public delegate void WaypointChangedEventHandler(object sender, int index);
    public delegate void FinishedEventHandler(object sender);

    public class NavigationHelper
    {
        public ObservableCollection<Coordinate> Waypoints = new ObservableCollection<Coordinate>();

        private int _currentindex;
        private readonly DispatcherTimer _recordcoordinates = new DispatcherTimer();
        private readonly DispatcherTimer _navigate = new DispatcherTimer();

        private Character _user;

        public event WaypointChangedEventHandler WaypointIndexChanged;
        public event FinishedEventHandler NavigationFinished;
        public double Sensitivity = 1.0;
        public bool Loop = true;
        public bool IsPlaying = false;

        private readonly Random _randNum = new Random();
        private readonly Stopwatch _jumpTimer = new Stopwatch();
        private readonly Stopwatch _stuckTimer = new Stopwatch();
        private int _jumpRand;

        private Thread _navigationThread;
        private bool _continue = true;
        private Coordinate _previousCoordinate = null;
        // Invoke the Changed event; called whenever list changes
        protected virtual void OnWaypointChanged()
        {


            if (_currentindex == Waypoints.Count)
            {
                if (Waypoints[0].setHeading)
                {
                    Keyboard.KeyBoardHelper.KeyUp(Keys.W);
                    _user.Heading = Waypoints[0].Heading;

                    //Keyboard.KeyBoardHelper.KeyPress(Keys.D6);
                }

            } else
            {
                if (Waypoints[_currentindex].setHeading)
                {
                    Keyboard.KeyBoardHelper.KeyUp(Keys.W);
                    _user.Heading = Waypoints[_currentindex].Heading;
                   // Keyboard.KeyBoardHelper.KeyPress(Keys.D6);
                }  
            }

            

            if (WaypointIndexChanged != null)
                WaypointIndexChanged(this, _currentindex);
        }



        private void GrabUser()
        {

            long startAddress = MemoryLocations.Database["charmap"];
            _user = new Character(startAddress);

        }

        public void CleanWaypoints(double sensitivity)
        {
            for(int i = Waypoints.Count - 1; i > 0; i--)
            {
                if (Waypoints[i].Distance2D(Waypoints[i - 1]) < sensitivity)
                {
                    Waypoints.RemoveAt(i-1);
                }
            }
        }



        ~NavigationHelper()
        {
            Debug.Print("Destructor Called For Navigation Helper");
            Keyboard.KeyBoardHelper.KeyUp(Keys.W);

            if (_navigationThread != null)
                if (_navigationThread.IsAlive)
                {
                    _navigationThread.Abort();
                    while (_navigationThread.IsAlive)
                    {

                    }
                }
        }

        public NavigationHelper()
        {

            Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture("en-US");
            Thread.CurrentThread.CurrentUICulture = CultureInfo.CreateSpecificCulture("en-US");

            _stuckTimer.Reset();

            if (MemoryLocations.Database.Count == 0)
                return;

            _recordcoordinates.Tick += thread_Record_Tick;
            _recordcoordinates.Interval = new TimeSpan(0, 0, 0, 0, 100);

            _navigate.Tick += thread_Navigate_Tick;
            _navigate.Interval = new TimeSpan(0, 0, 0, 0, 100);
            
            GrabUser();
        }


        public int FindNearestIndex()
        {
            float minDistance = 9999.0f;
            int i = 0;

            _user.Refresh();

            foreach (Coordinate currcoordinate in Waypoints)
            {
                float currDistance = currcoordinate.Distance2D(_user.Coordinate);

                if (currDistance < minDistance)
                {
                    minDistance = currDistance;
                    i = Waypoints.IndexOf(currcoordinate);
                }
            }

            return i;
        }


        public void Record()
        {
            _navigate.Stop();
            _recordcoordinates.Start();
        }
        public void StopRecording()
        {

            _recordcoordinates.Stop();
        }

        public void Start()
        {
            _recordcoordinates.Stop();

            _stuckTimer.Reset();
            _jumpRand = _randNum.Next(25, 50);
            _jumpTimer.Reset();
            _jumpTimer.Start();

            IsPlaying = true;
            _continue = true;

            _currentindex = FindNearestIndex();
            OnWaypointChanged();

            Keyboard.KeyBoardHelper.KeyDown(Keys.W);


            if (_navigationThread != null)
                if (_navigationThread.IsAlive)
                {
                    _navigationThread.Abort();
                    while (_navigationThread.IsAlive)
                    {
                    }
                }


            _navigationThread = new Thread(new ThreadStart(navigate_worker));

            _navigationThread.Start();

            while (!_navigationThread.IsAlive)
            {
            }

            Debug.Print("Thread Started");

            
            //_navigate.Start();

        }


        public void Stop()
        {

            _jumpTimer.Stop();
            IsPlaying = false;
            //_navigate.Stop();

            _continue = false;

            //_navigationThread.Join();

            //if (_navigationThread != null)
            //    while (_navigationThread.IsAlive)
            //    {
            //    }

            //Keyboard.KeyBoardHelper.KeyUp(Keys.W);
        }

        public void Resume()
        {

            _stuckTimer.Reset();
            _jumpTimer.Reset();
            _jumpTimer.Start();

            _continue = true;
            _jumpRand = _randNum.Next(25, 50);

            IsPlaying = true;
            Keyboard.KeyBoardHelper.KeyDown(Keys.W);

            if (_navigationThread != null)
                if (_navigationThread.IsAlive)
                {
                    _navigationThread.Abort();
                    while (_navigationThread.IsAlive)
                    {
                    }
                }


            _navigationThread = new Thread(new ThreadStart(navigate_worker));
            _navigationThread.Start();

            while (!_navigationThread.IsAlive)
            {
            }

            Debug.Print("Thread Started");
            //_navigate.Start();

        }

        public void Save(string filePath )
        {
            using (System.IO.StreamWriter file = new System.IO.StreamWriter(filePath))
            {
                foreach (Coordinate coord in Waypoints)
                {

                    if(coord.setHeading)
                    {
                        file.WriteLine(coord.X + "," + coord.Y + "," + coord.Z + "," + coord.Heading);
                    } else
                    {
                        file.WriteLine(coord.X + "," + coord.Y + "," + coord.Z);
                    }
                    

                }
            }
        }

        public void Load(string filePath)
        {
            using (System.IO.StreamReader file = new System.IO.StreamReader(filePath))
            {
                string line;

                while((line = file.ReadLine()) != null)
                {
                    List<string> results = line.Split(Convert.ToChar(",")).ToList();

                    Coordinate newCoordinate = new Coordinate(float.Parse(results[0]), float.Parse(results[1]), float.Parse(results[2]));
                    newCoordinate.setHeading = results.Count == 4;
                    if (newCoordinate.setHeading)
                    {
                        newCoordinate.Heading = float.Parse(results[3]);
                    }
                    Waypoints.Add(newCoordinate);

                }


            }
        }


        private void navigate_worker()
        {

            while (_continue)
            {
                if (_jumpTimer.Elapsed.Seconds > _jumpRand)
                {
                    Keyboard.KeyBoardHelper.KeyPress(Keys.Space);

                    _jumpTimer.Reset();
                    _jumpTimer.Start();

                    _jumpRand = _randNum.Next(25, 50);
                }

                if (Waypoints[_currentindex].setHeading)
                {
                    Stop();

                    if (NavigationFinished != null)
                        NavigationFinished(this);

                    break;
                }

                Keyboard.KeyBoardHelper.KeyDown(Keys.W);

                if (_currentindex >= Waypoints.Count)
                {
                    if (Loop)
                    {
                        _currentindex = 0;

                        OnWaypointChanged();
                    }
                    else
                    {
                        Stop();

                        if (NavigationFinished != null)
                            NavigationFinished(this);

                        break;
                        //return;
                    }
                }

                _user.Refresh();

                float newHeading = _user.Coordinate.AngleTo(Waypoints[_currentindex]);
                _user.Heading = newHeading;

                if(_previousCoordinate == null)
                {
                    _previousCoordinate = _user.Coordinate;
                    _stuckTimer.Reset();
                } else
                {
                    if(_user.Coordinate.Distance2D(_previousCoordinate) < 1.0)
                    {
                        if (_stuckTimer.IsRunning)
                        {

                            if (_stuckTimer.Elapsed.Seconds >= 2)
                            {
                                Debug.Print("Stuck...Trying to jump out");
                                Keyboard.KeyBoardHelper.KeyPress(Keys.Space);
                            }

                            if (_stuckTimer.Elapsed.Seconds >= 8)
                            {
                                Debug.Print("Stuck for over 8 seconds...");
                                Stop();

                                if (NavigationFinished != null)
                                    NavigationFinished(this);

                                break;
                            }

                        } else
                        {
                            _stuckTimer.Reset();
                            _stuckTimer.Start();
                        }
                    } else
                    {
                        _stuckTimer.Reset();
                        _previousCoordinate = _user.Coordinate;
                    }
                }

                if (_user.Coordinate.Distance2D(Waypoints[_currentindex]) < Sensitivity)
                {
                    _currentindex++;
                    OnWaypointChanged();
                }
            }

            Keyboard.KeyBoardHelper.KeyUp(Keys.W);

        }


        private void thread_Navigate_Tick(object sender, EventArgs e)
        {



            if(_jumpTimer.Elapsed.Seconds > _jumpRand )
            {
                Keyboard.KeyBoardHelper.KeyPress(Keys.Space);

                _jumpTimer.Reset();
                _jumpTimer.Start();

                _jumpRand = _randNum.Next(25, 50);
            }

            Keyboard.KeyBoardHelper.KeyDown(Keys.W);

            if (_currentindex >= Waypoints.Count)
            {
                if (Loop)
                {
                    _currentindex = 0;
                    OnWaypointChanged();
                } else
                {
                    Stop();

                    if (NavigationFinished != null)
                        NavigationFinished(this);

                    return;
                }
            }

            _user.Refresh();

            float newHeading = _user.Coordinate.AngleTo(Waypoints[_currentindex]);
            _user.Heading = newHeading;

            if (_user.Coordinate.Distance2D(Waypoints[_currentindex]) < Sensitivity)
            {
                _currentindex++;
                OnWaypointChanged();
            }
        }

        private void thread_Record_Tick(object sender, EventArgs e)
        {
            _user.Refresh();

            if (Waypoints.Count == 0)
            {
                Waypoints.Add(_user.Coordinate);
            }
            else
            {
                if (Waypoints.Last().Distance(_user.Coordinate) > 1.5)
                {
                    Waypoints.Add(_user.Coordinate);
                }

            }

        }


    }
}
