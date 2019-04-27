using System;
using System.Net;
using V5RPC;

namespace Simuro5v5.Strategy
{
    public class StrategyManagerRPC
    {
        int blue_local_port;
        int yellow_local_port;

        public IStrategyRPC BlueStrategy { get; private set; }
        public IStrategyRPC YellowStrategy { get; private set; }

        public TeamInfo BlueTeamInfo { get; set; }
        public TeamInfo YellowTeamInfo { get; set; }

        //public bool IsBlueReady => BlueStrategy != null;
        //public bool IsYellowReady => YellowStrategy != null;

        public bool IsBlueReady;
        public bool IsYellowReady;

        public StrategyManagerRPC() { }

        IPEndPoint ParseEndPoint(string endpoint)
        {
            string addr;
            int port;

            try
            {
                if (endpoint.Contains(":"))
                {
                    var ep = endpoint.Split(':');
                    addr = ep[0];
                    port = int.Parse(ep[1]);
                }
                else
                {
                    addr = endpoint;
                    port = 0;
                }

                return new IPEndPoint(IPAddress.Parse(addr), port);
            }
            catch (Exception)
            {
                throw new FormatException("Error endpoint: " + endpoint);
            }
        }

        public void ConnectBlue(string endpoint)
        {
            var ep = ParseEndPoint(endpoint);
            if (ep.Port == 0)
            {
                ep.Port = Config.StrategyConfig.BlueStrategyPort;
            }
            BlueStrategy = new RPCStrategy(ep);
            BlueTeamInfo = BlueStrategy.GetTeamInfo();
            IsBlueReady = true;
        }

        public void ConnectYellow(string endpoint)
        {
            var ep = ParseEndPoint(endpoint);
            if (ep.Port == 0)
            {
                ep.Port = Config.StrategyConfig.YellowStrategyPort;
            }
            YellowStrategy = new RPCStrategy(ep);
            YellowTeamInfo = YellowStrategy.GetTeamInfo();
            IsYellowReady = true;
        }

        public void CloseBlue()
        {
            BlueStrategy.Close();
            BlueStrategy = null;
            IsBlueReady = false;
        }

        public void CloseYellow()
        {
            YellowStrategy.Close();
            YellowStrategy = null;
            IsYellowReady = true;
        }
    }

    public interface IStrategyRPC
    {
        TeamInfo GetTeamInfo();
        void OnMatchStart();
        void OnMatchStop();
        void OnRoundStart();
        void OnRoundStop();
        WheelInfo GetInstruction(SideInfo sideInfo);
        PlacementInfo GetPlacement(SideInfo sideInfo);

        void Close();
    }

    public class RPCStrategy : IStrategyRPC
    {
        StrategyClient client;

        public RPCStrategy(IPEndPoint endpoint)
        {
            client = new StrategyClient(endpoint)
            {
                Timeout = Config.StrategyConfig.ConnectTimeout,
            };
        }

        public TeamInfo GetTeamInfo()
        {
            return client.GetTeamInfo().ToNative();
        }

        public WheelInfo GetInstruction(SideInfo sideInfo)
        {
            var wheels = client.GetInstruction(sideInfo.ToProto());
            var rv = new WheelInfo { Wheels = new Wheel[Const.RobotsPerTeam] };
            for (int i = 0; i < Const.RobotsPerTeam; i++)
            {
                rv.Wheels[i] = wheels[i].ToNative();
            }
            return rv;
        }

        public PlacementInfo GetPlacement(SideInfo sideInfo)
        {
            var placement = client.GetPlacement(sideInfo.ToProto());
            var rv = new PlacementInfo { Robots = new Robot[Const.RobotsPerTeam] };
            for (int i = 0; i < Const.RobotsPerTeam; i++)
            {
                rv.Robots[i] = placement.Robots[i].ToNative();
                rv.Ball = placement.Ball.ToNative();
            }
            return rv;
        }

        public void OnMatchStart()
        {
            client.OnEvent(V5RPC.Proto.EventType.MatchStart, new V5RPC.Proto.EventArguments());
        }

        public void OnMatchStop()
        {
            client.OnEvent(V5RPC.Proto.EventType.MatchStop, new V5RPC.Proto.EventArguments());
        }

        public void OnRoundStart()
        {
            client.OnEvent(V5RPC.Proto.EventType.RoundStart, new V5RPC.Proto.EventArguments());
        }

        public void OnRoundStop()
        {
            client.OnEvent(V5RPC.Proto.EventType.RoundStop, new V5RPC.Proto.EventArguments());
        }

        public void Close()
        {
            client.Dispose();
        }
    }

    /// <summary>
    /// Protobuf的类和内部使用的类的互相转化
    /// </summary>
    public static class ProtoConverter
    {
        public static Vector2D ToNative(this V5RPC.Proto.Vector2 proto)
        {
            return new Vector2D
            {
                x = proto.X,
                y = proto.Y
            };
        }

        public static Ball ToNative(this V5RPC.Proto.Ball proto)
        {
            return new Ball
            {
                pos = proto.Position.ToNative()
            };
        }

        public static Wheel ToNative(this V5RPC.Proto.Wheel proto)
        {
            return new Wheel
            {
                left = proto.LeftSpeed,
                right = proto.RightSpeed
            };
        }

        public static Robot ToNative(this V5RPC.Proto.Robot proto)
        {
            return new Robot
            {
                pos = proto.Position.ToNative(),
                rotation = proto.Rotation,
                wheel = new Wheel
                {
                    left = proto.Wheel.LeftSpeed,
                    right = proto.Wheel.RightSpeed
                }
            };
        }

        public static TeamInfo ToNative(this V5RPC.Proto.TeamInfo proto)
        {
            return new TeamInfo
            {
                Name = proto.TeamName
            };
        }

        public static V5RPC.Proto.Vector2 ToProto(this Vector2D native)
        {
            return new V5RPC.Proto.Vector2 { X = native.x, Y = native.y };
        }

        public static V5RPC.Proto.Ball ToProto(this Ball native)
        {
            return new V5RPC.Proto.Ball { Position = native.pos.ToProto() };
        }

        public static V5RPC.Proto.Robot ToProto(this Robot native)
        {
            return new V5RPC.Proto.Robot
            {
                Position = native.pos.ToProto(),
                Rotation = native.rotation,
                Wheel = new V5RPC.Proto.Wheel
                {
                    LeftSpeed = native.wheel.left,
                    RightSpeed = native.wheel.right
                }
            };
        }

        public static V5RPC.Proto.Robot ToProto(this OpponentRobot native)
        {
            return new V5RPC.Proto.Robot
            {
                Position = native.pos.ToProto(),
                Rotation = native.rotation,
                Wheel = new V5RPC.Proto.Wheel()
            };
        }

        public static V5RPC.Proto.Field ToProto(this SideInfo native)
        {
            var field = new V5RPC.Proto.Field();
            for (int i = 0; i < 5; i++)
            {
                field.OurRobots.Add(native.home[i].ToProto());
                field.OpponentRobots.Add(native.opp[i].ToProto());
            }
            field.Ball = native.currentBall.ToProto();
            field.TickTotal = native.TickMatch;
            field.TickRound = native.TickRound;
            return field;
        }
    }
}
