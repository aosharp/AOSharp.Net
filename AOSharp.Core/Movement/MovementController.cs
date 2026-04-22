using AOSharp.Common.GameData;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using AOSharp.Common.Unmanaged.Imports;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using AOSharp.Core.UI;
using SmokeLounge.AOtomation.Messaging.GameData;

namespace AOSharp.Core.Movement
{
    public class MovementController
    {
        private const float UpdateInterval = 0.2f;
        private const float UnstuckInterval = 5f;
        private const float UnstuckThreshold = 2f;

        public bool IsNavigating => _paths.Count != 0;

        private double _nextUpdate = 0;
        private double _nextUstuckCheck = Time.NormalTime;
        private float _lastDist = 0f;
        private bool _drawPath;
        protected Queue<Path> _paths = new Queue<Path>();

        private ConcurrentQueue<MovementAction> _movementActionQueue = new ConcurrentQueue<MovementAction>();

        public EventHandler DestinationReached;

        public static MovementController Instance { get; internal set; }

        public MovementController(bool drawPath = true)
        {
            _drawPath = drawPath;
        }

        public static void Set(MovementController movementcontroller)
        {
            Instance = movementcontroller;
        }

        public virtual void Update()
        {
            try
            {
                while (_movementActionQueue.TryDequeue(out MovementAction action))
                    ChangeMovement(action);

                if (_paths.Count == 0)
                    return;

                if (!DynelManager.LocalPlayer.IsMoving)
                    SetMovement(MovementAction.ForwardStart);

                if (!_paths.Peek().Initialized)
                    _paths.Peek().OnPathStart();

                Vector3 waypoint;
                if (!_paths.Peek().GetNextWaypoint(out waypoint))
                {
                    Path path = _paths.Dequeue();
                    path.OnPathFinished();

                    if (_paths.Count == 0 && path.StopAtDest)
                    {
                        SetMovement(MovementAction.ForwardStop);
                        DestinationReached?.Invoke(null, null);
                    }

                    return;
                }

                if (Time.NormalTime > _nextUstuckCheck)
                {
                    float currentDist = DynelManager.LocalPlayer.Position.DistanceFrom(waypoint);

                    if (_lastDist - currentDist <= UnstuckThreshold)
                        OnStuck();

                    _lastDist = currentDist;
                    _nextUstuckCheck = Time.NormalTime + UnstuckInterval;
                }

                LookAt(waypoint);

                if (Time.NormalTime > _nextUpdate)
                {
                    SetMovement(MovementAction.Update);
                    _nextUpdate = Time.NormalTime + UpdateInterval;
                }

                if (_drawPath)
                    _paths.Peek().Draw();
            }
            catch (Exception e)
            {
                Chat.WriteLine($"This shouldn't happen pls report (MovementController): {e.Message}");
            }
        }

        public void Halt()
        {
            _paths.Clear();

            SetMovement(MovementAction.FullStop);
        }

        public void Follow(Identity identity)
        {
            Network.Send(new FollowTargetMessage
            {
                Type = FollowTargetType.Target,
                Info = new FollowTargetMessage.TargetInfo
                {
                    Target = identity
                }
            });
        }

        public virtual void SetDestination(Vector3 pos)
        {
            SetDestination(pos, out _);
        }

        public virtual void AppendDestination(Vector3 pos)
        {
            AppendDestination(pos, out _);
        }

        public virtual bool SetDestination(Vector3 pos, out Path path)
        {
            return SetPath(new List<Vector3> { pos }, out path);
        }

        public virtual bool AppendDestination(Vector3 pos, out Path path)
        {
            return AppendPath(new List<Vector3> { pos }, out path);
        }

        public void SetPath(List<Vector3> waypoints)
        {
            SetPath(waypoints, out _);
        }

        public void AppendPath(List<Vector3> waypoints)
        {
            AppendPath(waypoints, out _);
        }

        public virtual bool SetPath(List<Vector3> waypoints, out Path path)
        {
            _paths.Clear();
            return AppendPath(waypoints, out path);
        }

        public virtual bool AppendPath(List<Vector3> waypoints, out Path path)
        {
            path = new Path(waypoints);
            AppendPath(path);
            return true;
        }

        public virtual void SetPath(Path path)
        {
            _paths.Clear();
            AppendPath(path);
        }

        public virtual void AppendPath(Path path)
        {
            _paths.Enqueue(path);
        }

        public void LookAt(Vector3 pos)
        {
            Vector3 myPos = DynelManager.LocalPlayer.Position;
            myPos.Y = 0;
            Vector3 dstPos = pos;
            dstPos.Y = 0;
            DynelManager.LocalPlayer.Rotation = Quaternion.FromTo(myPos, dstPos);
        }

        protected virtual void OnStuck()
        {
            //Chat.WriteLine("Stuck!?");
        }

        //Must be called from game loop!
        private static void ChangeMovement(MovementAction action)
        {
            if (action == MovementAction.LeaveSit)
            {
                Network.Send(new CharacterActionMessage()
                {
                    Action = CharacterActionType.StandUp
                });
            }
            else
            {
                IntPtr pEngine = N3Engine_t.GetInstance();

                if (pEngine == IntPtr.Zero)
                    return;

                N3EngineClientAnarchy_t.MovementChanged(pEngine, action, 0, 0, true);
            }       
        }

        public void SetMovement(MovementAction action)
        {
            _movementActionQueue.Enqueue(action);
        }
    }

    public class Path
    {
        public float NodeReachedDist = 1;
        public float DestinationReachedDist = 1;
        public bool StopAtDest = true;
        public bool Initialized = false;
        protected Queue<Vector3> _waypoints = new Queue<Vector3>();
        public Action DestinationReachedCallback;

        public Path(Vector3 destination) : this(new List<Vector3>() { destination })
        {

        }

        public Path(List<Vector3> waypoints)
        {
            SetWaypoints(waypoints);
        }

        public virtual void OnPathStart()
        {
            Initialized = true;
        }

        public virtual void OnPathFinished()
        {
            DestinationReachedCallback?.Invoke();
        }


        protected void SetWaypoints(List<Vector3> waypoints)
        {
            _waypoints.Clear();

            foreach (Vector3 waypoint in waypoints)
            {
                if (DynelManager.LocalPlayer.Position.DistanceFrom(waypoint) > NodeReachedDist)
                    _waypoints.Enqueue(waypoint);
            }
        }

        internal bool GetNextWaypoint(out Vector3 waypoint)
        {
            if (_waypoints.Count == 0)
            {
                waypoint = Vector3.Zero;
                return false;
            }

            if (DynelManager.LocalPlayer.Position.DistanceFrom(_waypoints.Peek()) <= (_waypoints.Count > 1 ? NodeReachedDist : DestinationReachedDist))
            {
                _waypoints.Dequeue();

                if (_waypoints.Count == 0)
                {
                    waypoint = Vector3.Zero;
                    return false;
                }
            }

            waypoint = _waypoints.Peek();
            return true;
        }

        internal void Draw()
        {
            Vector3 lastWaypoint = DynelManager.LocalPlayer.Position;
            foreach (Vector3 waypoint in _waypoints)
            {
                Debug.DrawLine(lastWaypoint, waypoint, DebuggingColor.Yellow);
                lastWaypoint = waypoint;
            }
        }
    }
}
