﻿using System;
using System.Collections;
using UnityEngine;
using Simuro5v5;
using Simuro5v5.Config;
using Simuro5v5.Strategy;
using Event = Simuro5v5.EventSystem.Event;

/// <summary>
/// 这个类用来维护比赛的状态，以及负责协调策略与场地
/// </mmary>
public class PlayMain : MonoBehaviour
{
    /// <summary>
    /// 在比赛运行的整个时间内为真
    /// </summary>
    public bool Started { get; private set; }

    /// <summary>
    /// 在比赛暂停时为真
    /// </summary>
    public bool Paused { get; private set; }

    // 策略已经加载成功
    public bool LoadSucceed => StrategyManager.IsBlueReady && StrategyManager.IsYellowReady;

    public static GameObject Singleton;
    public StrategyManager StrategyManager { get; private set; }
    public MatchInfo GlobalMatchInfo { get; private set; }
    public ObjectManager ObjectManager { get; private set; }

    public delegate void TimedPauseCallback();
    bool TimedPausing { get; set; }
    readonly object timedPausingLock = new object();

    // 进入场景之后
    void OnEnable()
    {
        if (Singleton != null)
        {
            if (gameObject != Singleton)
            {
                // 此时新的gameObject已经创建，调用DestroyImmediate而不是Destroy以确保新的go不会与已存在的go碰撞
                DestroyImmediate(gameObject);
            }
            else
            {
                ObjectManager.Pause();
            }
            // 激活单例
            Singleton.SetActive(true);
        }
    }

    IEnumerator Start()
    {
        ConfigManager.ReadConfigFile("config.json");
        Singleton = gameObject;
        DontDestroyOnLoad(GameObject.Find("/Entity"));

        StrategyManager = new StrategyManager();
        GlobalMatchInfo = MatchInfo.NewDefaultPreset();
        // 绑定物体
        ObjectManager = new ObjectManager();
        ObjectManager.RebindObject();
        ObjectManager.RebindMatchInfo(GlobalMatchInfo);
        Event.Register(Event.EventType0.PlaySceneExited, SceneExited);
        // Event.Register(Event.EventType1.GetGoal, OnGetGoal);

        // 等待当前帧渲染完毕后暂停，确保还原后的场景显示到屏幕上
        yield return new WaitForEndOfFrame();
        ObjectManager.Pause();
    }

    /// <summary>
    /// 维护比赛状态，根据裁判的判定结果执行不同的动作。<br>
    /// 
    /// 裁判的判定：
    /// 裁判共有3类判定结果：结束一个阶段（NextPhase和GameOver）、正常比赛（NormalMatch）以及摆位（xxxKick）；
    /// 另外要注意的是，裁判不会主动修改MatchInfo中的任何信息，所有信息由JudgeResult返回。<br>
    ///
    /// 判定结果的执行：
    /// 结束一个阶段则结束一个阶段，并将时间设置为0；
    /// 正常比赛则从策略中获取轮速，并将时间加一，触发事件；
    /// 摆位则首先判断是否为进球引起的摆位（JudgeResult.WhoGoal），然后从策略中获取摆位信息，并将时间加一，触发事件。<br>
    /// 
    /// 进球：
    /// 进球会发生在摆位或者GameOver状态；
    /// 正常情况下进球会引起摆位，但是如果进入点球大战的“突然死亡”模式，有可能进球伴随着GameOver状态。<br>
    ///
    /// 比赛阶段：
    /// 一场比赛最多分为4个阶段：上半场、下半场、加时、点球大战；<br>
    /// 每个阶段都由一个摆位开始，由NextPhase或者GameOver结束；
    /// 每个阶段结束后，不会清空比分，但会清空比赛时间，将比赛时间设置为0以使得下一拍的裁判得知这是一个新的阶段；
    /// 每个阶段运行期间可能产生正常比赛和摆位两种状态。<br>
    /// 
    /// 比赛时间：
    /// 实际的比赛时间从1开始；
    /// 指定为0表示切换到了新的阶段，不算做实际比赛时间；
    /// 能够使得时间增加一的判定结果有正常比赛和摆位两个;<br>
    /// 
    /// 事件的触发：
    /// 时间每增加一之后就会触发MatchInfoUpdate事件，时间重置为0时不会触发该事件；
    /// 摆位时会触发AutoPlacement事件。<br>
    /// </summary>
    public void InMatchLoop()
    {
        if (!LoadSucceed || !Started || Paused) return;

        /// 从裁判中获取下一拍的动作。
        JudgeResult judgeResult = GlobalMatchInfo.Referee.Judge(GlobalMatchInfo);

        if (judgeResult.ResultType == ResultType.GameOver)
        {
            // 整场比赛结束
            Debug.Log("Game Over");

            // 判断是否进球
            switch(judgeResult.WhoGoal){
                case Side.Blue:
                    GlobalMatchInfo.Score.BlueScore++;
                    break;
                case Side.Yellow:
                    GlobalMatchInfo.Score.YellowScore++;
                    break;
            }

            StopMatch();
        }
        else if (judgeResult.ResultType == ResultType.NextPhase)
        {
            // 上阶段结束
            Debug.Log("next phase");
            GlobalMatchInfo.MatchPhase = GlobalMatchInfo.MatchPhase.NextPhase();
            GlobalMatchInfo.TickMatch = 0;      // 时间指定为0，使得下一拍的裁判得知新阶段的开始
        }
        else if (judgeResult.ResultType == ResultType.NormalMatch)
        {
            // 正常比赛
            UpdateWheelsToScene();
            // 时间加一，触发事件
            GlobalMatchInfo.TickMatch++;
            Event.Send(Event.EventType1.MatchInfoUpdate, GlobalMatchInfo);
        }
        else
        {
            // 摆位
            Debug.Log("placing...");

            // 判断是否进球
            switch(judgeResult.WhoGoal){
                case Side.Blue:
                    GlobalMatchInfo.Score.BlueScore++;
                    break;
                case Side.Yellow:
                    GlobalMatchInfo.Score.YellowScore++;
                    break;
            }

            void Callback()
            {
                UpdatePlacementToScene(judgeResult);
                // 时间加一，触发事件
                GlobalMatchInfo.TickMatch++;
                Event.Send(Event.EventType1.MatchInfoUpdate, GlobalMatchInfo);
                Event.Send(Event.EventType1.AutoPlacement, GlobalMatchInfo);

                PauseForSeconds(2, () => { });
            }

            if (GlobalMatchInfo.TickMatch > 0)
            {
                PauseForSeconds(2, Callback);
            }
            else
            {
                Callback();
            }
        }
    }

    /// <summary>
    /// 新比赛开始<br/>
    /// 比分、阶段信息清空，时间置为零，裁判信息清空；还原默认场地；通知策略
    /// </summary>
    public void StartMatch()
    {
        Started = false;

        GlobalMatchInfo.Score = new MatchScore();
        GlobalMatchInfo.TickMatch = 0;
        GlobalMatchInfo.MatchPhase = MatchPhase.FirstHalf;
        GlobalMatchInfo.Referee = new Referee();

        ObjectManager.SetToDefault();
        ObjectManager.SetStill();

        StrategyManager.Blue.OnMatchStart();
        StrategyManager.Yellow.OnMatchStart();

        Started = true;
        Paused = true;
        Event.Send(Event.EventType1.MatchStart, GlobalMatchInfo);
    }

    /// <summary>
    /// 停止比赛<br/>
    /// 会根据<parmref name="willNotifyStrategies">参数决定是否通知策略；
    /// 暂时保留赛场信息，等到下次StartMatch会重置赛场。
    /// </summary>
    /// <param name="willNotifyStrategies">是否向策略发送通知，如果是由于策略出现错误需要停止比赛，可以指定为false。默认为true</param>
    public void StopMatch(bool willNotifyStrategies=true)
    {
        Started = false;
        Paused = true;

        // GlobalMatchInfo.Score = new MatchScore();
        // GlobalMatchInfo.TickMatch = 0;
        // GlobalMatchInfo.MatchPhase = MatchPhase.FirstHalf;
        // GlobalMatchInfo.Referee = new Referee();

        // ObjectManager.SetToDefault();
        // ObjectManager.SetStill();
        ObjectManager.Pause();

        if (willNotifyStrategies)
        {
            StrategyManager.Blue.OnMatchStop();
            StrategyManager.Yellow.OnMatchStop();
        }
        Event.Send(Event.EventType1.MatchStop, GlobalMatchInfo);
    }

    /// <summary>
    /// 如果比赛开始则暂停比赛。
    /// </summary>
    public void PauseMatch()
    {
        if (Started)
        {
            Paused = true;
            ObjectManager.Pause();
        }
    }

    /// <summary>
    /// 继续比赛
    /// </summary>
    public void ResumeMatch()
    {
        if (Started)
        {
            Paused = false;
            ObjectManager.Resume();
        }
    }

    /// <summary>
    /// 从策略中获取轮速并输入到场地中
    /// </summary>
    void UpdateWheelsToScene()
    {
        WheelInfo wheelsBlue = StrategyManager.Blue.GetInstruction(GlobalMatchInfo.GetSide(Side.Blue));

        SideInfo yellow = GlobalMatchInfo.GetSide(Side.Yellow);
        if (GeneralConfig.EnableConvertYellowData)
        {
            yellow.ConvertToOtherSide();
        }
        WheelInfo wheelsYellow = StrategyManager.Yellow.GetInstruction(yellow);

        wheelsBlue.Normalize();     //轮速规整化
        wheelsYellow.Normalize();   //轮速规整化

        ObjectManager.SetBlueWheels(wheelsBlue);
        ObjectManager.SetYellowWheels(wheelsYellow);
    }

    /// <summary>
    /// 从策略中获取摆位信息并做检查和修正，之后输入到场地中。
    /// </summary>
    /// <param name="judgeResult">摆位的原因信息</param>
    void UpdatePlacementToScene(JudgeResult judgeResult)
    {
        var currMi = (MatchInfo)GlobalMatchInfo.Clone();
        PlacementInfo blueInfo;
        PlacementInfo yellowInfo;

        switch (judgeResult.WhoisFirst)
        {
            case Side.Blue:
                // 蓝方先摆位
                blueInfo = StrategyManager.Blue.GetPlacement(currMi.GetSide(Side.Blue));
                // 将蓝方返回的数据同步到currMi
                currMi.UpdateFrom(blueInfo.Robots, Side.Blue);
                // 黄方后摆位
                yellowInfo = StrategyManager.Yellow.GetPlacement(currMi.GetSide(Side.Yellow));

                // 转换数据
                if (GeneralConfig.EnableConvertYellowData)
                    yellowInfo.ConvertToOtherSide();

                break;
            case Side.Yellow:
                // 黄方先摆位
                yellowInfo = StrategyManager.Yellow.GetPlacement(currMi.GetSide(Side.Yellow));
                // 由于右攻假设，需要先将黄方数据转换
                if (GeneralConfig.EnableConvertYellowData)
                    yellowInfo.ConvertToOtherSide();

                // 将黄方返回的数据同步到currMi
                currMi.UpdateFrom(yellowInfo.Robots, Side.Yellow);
                // 蓝方后摆位
                blueInfo = StrategyManager.Blue.GetPlacement(currMi.GetSide(Side.Blue));
                break;
            default:
                throw new ArgumentException("Side cannot be Nobody");
        }

        // 从两方数据拼接MatchInfo，球的数据取决于judgeResult
        var mi = new MatchInfo(blueInfo, yellowInfo, judgeResult.Actor);
        GlobalMatchInfo.Referee.JudgeAutoPlacement(mi, judgeResult);

        // 设置场地
        ObjectManager.SetBluePlacement(mi.BlueRobots);
        ObjectManager.SetYellowPlacement(mi.YellowRobots);
        ObjectManager.SetBallPlacement(mi.Ball);

        ObjectManager.SetStill();
    }

    /// <summary>
    /// 从指定的endpoint字符串中加载双方策略
    /// </summary>
    /// <param name="blue_ep"></param>
    /// <param name="yellow_ep"></param>
    /// <returns></returns>
    /// <exception cref="StrategyException">
    /// 加载失败则抛出该错误
    /// </exception>
    public bool LoadStrategy(string blue_ep, string yellow_ep)
    {
        var factory = new StrategyFactory
        {
            BlueEP = blue_ep,
            YellowEP = yellow_ep
        };
        StrategyManager.StrategyFactory = factory;

        try
        {
            StrategyManager.ConnectBlue();
        }
        catch (Exception e)
        {
            throw new StrategyException(Side.Blue, e);
        }

        try
        {
            StrategyManager.ConnectYellow();
        }
        catch (Exception e)
        {
            throw new StrategyException(Side.Yellow, e);
        }

        return true;
    }

    /// <summary>
    /// 移除一方的策略
    /// </summary>
    public void RemoveStrategy(Side side)
    {
        switch (side)
        {
            case Side.Blue:
                StrategyManager.Blue.Close();
                break;
            case Side.Yellow:
                StrategyManager.Yellow.Close();
                break;
        }
    }

    /// <summary>
    /// 移除两方的策略
    /// </summary>
    public void RemoveStrategy()
    {
        RemoveStrategy(Side.Blue);
        RemoveStrategy(Side.Yellow);
    }

    void OnGetGoal(object obj)
    {
        Side who = (Side)obj;
        switch (who)
        {
            case Side.Blue:
                GlobalMatchInfo.Score.BlueScore++;
                break;
            case Side.Yellow:
                GlobalMatchInfo.Score.YellowScore++;
                break;
        }
    }

    /// <summary>
    /// 暂停<parmref name="sec">秒，然后执行<parmref name="callback">。
    /// </summary>
    private void PauseForSeconds(int sec, TimedPauseCallback callback)
    {
        if (sec > 0)
        {
            PauseMatch();
            StartCoroutine(_PauseCoroutine(sec, callback));
        }
    }

    IEnumerator _PauseCoroutine(float sec, TimedPauseCallback callback)
    {
        yield return new WaitUntil(delegate ()
        {
            lock (timedPausingLock)
            {
                return TimedPausing == false;
            }
        });
        TimedPausing = true;
        yield return new WaitForSecondsRealtime(sec);
        ResumeMatch();
        callback();
        TimedPausing = false;
    }

    void SceneExited()
    {
        gameObject.SetActive(false);
    }

    private void OnApplicationQuit()
    {
        Event.Send(Event.EventType0.PlatformExiting);
    }

    class StrategyFactory : IStrategyFactory
    {
        public string BlueEP { get; set; }
        public string YellowEP { get; set; }
        public IStrategy CreateBlue()
        {
            try
            {
                return new RPCStrategy(BlueEP);
            }
            catch (Exception e)
            {
                throw new StrategyException(Side.Blue, e);
            }
        }

        public IStrategy CreateYellow()
        {
            try
            {
                return new RPCStrategy(YellowEP);
            }
            catch (Exception e)
            {
                throw new StrategyException(Side.Yellow, e);
            }
        }
    }
}