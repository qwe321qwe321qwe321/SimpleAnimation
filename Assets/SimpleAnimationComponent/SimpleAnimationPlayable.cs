using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using System;

/// <summary>
/// The PlayableBehaviour of SimpleAnimation.
/// </summary>
public partial class SimpleAnimationPlayable : PlayableBehaviour
{
    LinkedList<QueuedState> m_StateQueue;
    StateManagement m_States;
    bool m_Initialized;

    bool m_KeepStoppedPlayablesConnected = true;
    public bool keepStoppedPlayablesConnected
    {
        get { return m_KeepStoppedPlayablesConnected; }
        set
        {
            if (value != m_KeepStoppedPlayablesConnected)
            {
                m_KeepStoppedPlayablesConnected = value;
            }
        }
    }

    void UpdateStoppedPlayablesConnections()
    {
        for (int i = 0; i < m_States.Count; i++)
        {
            StateInfo state = m_States[i];
            if (state == null)
                continue;
            if (state.enabled)
                continue;
            if (keepStoppedPlayablesConnected)
            {
                ConnectInput(state.index);
            }
            else
            {
                DisconnectInput(state.index);
            }
        }
    }

    protected Playable m_ActualPlayable;
    protected Playable self { get { return m_ActualPlayable; } }
    public Playable playable { get { return self; } }
    protected PlayableGraph graph { get { return self.GetGraph(); } }

    AnimationMixerPlayable m_Mixer;

    /// <summary>
    /// Callback when there is no playing states anymore.
    /// </summary>
    public System.Action onDone = null;
    public SimpleAnimationPlayable()
    {
        m_States = new StateManagement();
        this.m_StateQueue = new LinkedList<QueuedState>();
    }

    public Playable GetInput(int index)
    {
        if (index >= m_Mixer.GetInputCount())
            return Playable.Null;

        return m_Mixer.GetInput(index);
    }

    public override void OnPlayableCreate(Playable playable)
    {
        m_ActualPlayable = playable;

        var mixer = AnimationMixerPlayable.Create(graph, 1, true);
        m_Mixer = mixer;

        self.SetInputCount(1);
        self.SetInputWeight(0, 1);
        graph.Connect(m_Mixer, 0, self, 0);
    }

    public IEnumerable<IState> GetStates()
    {
        return new StateEnumerable(this);
    }

    public IState GetState(string name)
    {
        StateInfo state = m_States.FindState(name);
        if (state == null)
        {
            return null;
        }

        return new StateHandle(this, state.index, state.playable);
    }

    private StateInfo DoAddClip(string name, AnimationClip clip)
    {
        //Start new State
        StateInfo newState = m_States.InsertState();
        newState.Initialize(name, clip, clip.wrapMode);
        //Find at which input the state will be connected
        int index = newState.index;

        //Increase input count if needed
        if (index == m_Mixer.GetInputCount())
        {
            m_Mixer.SetInputCount(index + 1);
        }

        var clipPlayable = AnimationClipPlayable.Create(graph, clip);
        clipPlayable.SetApplyFootIK(false);
        clipPlayable.SetApplyPlayableIK(false);
        // 為non-looping的clip設定好duration的值
        if (!clip.isLooping || newState.wrapMode == WrapMode.Once)
        {
            clipPlayable.SetDuration(clip.length);
        }
        newState.SetPlayable(clipPlayable);
        newState.Pause();

        if (keepStoppedPlayablesConnected)
            ConnectInput(newState.index);

        return newState;
    }

    public bool AddClip(AnimationClip clip, string name)
    {
        StateInfo state = m_States.FindState(name);
        if (state != null)
        {
            Debug.LogError(string.Format("Cannot add state with name {0}, because a state with that name already exists", name));
            return false;
        }

        DoAddClip(name, clip);
        UpdateDoneStatus();
        InvalidateStates();

        return true;
    }

    public bool RemoveClip(string name)
    {
        StateInfo state = m_States.FindState(name);
        if (state == null)
        {
            Debug.LogError(string.Format("Cannot remove state with name {0}, because a state with that name doesn't exist", name));
            return false;
        }

        RemoveClones(state);
        InvalidateStates();
        m_States.RemoveState(state.index);
        return true;
    }

    public bool RemoveClip(AnimationClip clip)
    {
        InvalidateStates();
        return m_States.RemoveClip(clip);
    }

    public bool Play(string name)
    {
        StateInfo state = m_States.FindState(name);
        if (state == null)
        {
            Debug.LogError(string.Format("Cannot play state with name {0} because there is no state with that name", name));
            return false;
        }

        return Play(state.index);
    }

    /// <summary>
    /// 播放指定state(以index決定)
    /// </summary>
    private bool Play(int index)
    {
        // 將指定index的state的weight設定為1，其餘都為0
        for (int i = 0; i < m_States.Count; i++)
        {
            StateInfo state = m_States[i];
            if (state.index == index)
            {
                state.Enable();
                state.ForceWeight(1.0f);
            }
            else
            {
                DoStop(i);
            }
        }

        return true;
    }

    public bool PlayQueued(string name, QueueMode queueMode)
    {
        StateInfo state = m_States.FindState(name);
        if (state == null)
        {
            Debug.LogError(string.Format("Cannot queue Play to state with name {0} because there is no state with that name", name));
            return false;
        }

        return PlayQueued(state.index, queueMode);
    }

    bool PlayQueued(int index, QueueMode queueMode)
    {
        // 複製一個排進queue
        StateInfo newState = CloneState(index);

        if (queueMode == QueueMode.PlayNow)
        {
            Play(newState.index);
            return true;
        }

        m_StateQueue.AddLast(new QueuedState(StateInfoToHandle(newState), 0f));
        return true;
    }

    public void Rewind(string name)
    {
        StateInfo state = m_States.FindState(name);
        if (state == null)
        {
            Debug.LogError(string.Format("Cannot Rewind state with name {0} because there is no state with that name", name));
            return;
        }

        Rewind(state.index);
    }

    private void Rewind(int index)
    {
        m_States.SetStateTime(index, 0f);
    }

    public void Rewind()
    {
        for (int i = 0; i < m_States.Count; i++)
        {
            if (m_States[i] != null)
                m_States.SetStateTime(i, 0f);
        }
    }

    private void RemoveClones(StateInfo state)
    {
        var it = m_StateQueue.First;
        while (it != null)
        {
            var next = it.Next;

            StateInfo queuedState = m_States[it.Value.state.index];
            if (queuedState.parentState.index == state.index)
            {
                m_StateQueue.Remove(it);
                DoStop(queuedState.index);
            }

            it = next;
        }
    }

    public bool Stop(string name)
    {
        StateInfo state = m_States.FindState(name);
        if (state == null)
        {
            Debug.LogError(string.Format("Cannot stop state with name {0} because there is no state with that name", name));
            return false;
        }

        DoStop(state.index);

        UpdateDoneStatus();

        return true;
    }

    private void DoStop(int index)
    {
        StateInfo state = m_States[index];
        if (state == null)
            return;
        // 如果state本身是clone出來的，則需要將其從graph中移除。
        m_States.StopState(index, state.isClone);
        if (!state.isClone)
        {
            RemoveClones(state);
        }
    }

    public bool StopAll()
    {
        for (int i = 0; i < m_States.Count; i++)
        {
            DoStop(i);
        }

        playable.SetDone(true);

        return true;
    }

    public bool IsPlaying()
    {
        return m_States.AnyStatePlaying();
    }

    public bool IsPlaying(string stateName)
    {
        StateInfo state = m_States.FindState(stateName);
        if (state == null)
            return false;

        return state.enabled || IsClonePlaying(state);
    }

    private bool IsClonePlaying(StateInfo state)
    {
        for (int i = 0; i < m_States.Count; i++)
        {
            StateInfo otherState = m_States[i];
            if (otherState == null)
                continue;

            if (otherState.isClone && otherState.enabled && otherState.parentState.index == state.index)
            {
                return true;
            }
        }

        return false;
    }

    public int GetClipCount()
    {
        int count=0;
        for (int i = 0; i < m_States.Count; i++)
        {
            if (m_States[i] != null)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// 設定state的fading相關參數
    /// </summary>
    /// <param name="time">要花多長時間fade</param>
    private void SetupLerp(StateInfo state, float targetWeight, float time)
    {
        // 以time參數來計算fading的速度，如果time=0則速度無限大。
        float travel = Mathf.Abs(state.weight - targetWeight);
        float newSpeed = time != 0f ? travel / time : Mathf.Infinity;

        // If we're fading to the same target as before but slower, assume CrossFade was called multiple times and ignore new speed
        if (state.fading && Mathf.Approximately(state.targetWeight, targetWeight) && newSpeed < state.fadeSpeed)
            return;

        state.FadeTo(targetWeight, newSpeed);
    }

    /// <summary>
    /// 在time時間內，將當前動畫fade成指定state。
    /// </summary>
    private bool Crossfade(int index, float time)
    {
        for (int i = 0; i < m_States.Count; i++)
        {
            StateInfo state = m_States[i];
            if (state == null)
                continue;

            // 找指定index的state
            if (state.index == index)
            {   
                m_States.EnableState(index);
            }
            // 如果是關閉的state就skip
            if (state.enabled == false)
                continue;
            // 如果是指定的index，則target weight是1.0，其餘的則是0
            float targetWeight = state.index == index ? 1.0f : 0.0f;
            // 設定state內的fading相關參數
            SetupLerp(state, targetWeight, time);
        }

        return true;
    }

    /// <summary>
    /// 複製指定State並加入graph，主要提供給queue使用。
    /// </summary>
    /// <param name="index"></param>
    /// <returns></returns>
    private StateInfo CloneState(int index)
    {
        StateInfo original = m_States[index];
        string newName = original.stateName + "Queued Clone";
        StateInfo clone = DoAddClip(newName, original.clip);
        clone.SetAsCloneOf(new StateHandle(this, original.index, original.playable));
        return clone;
    }

    /// <summary>
    /// 在time時間內，將當前動畫fade成指定state。
    /// </summary>
    public bool Crossfade(string name, float time)
    {
        StateInfo state = m_States.FindState(name);
        if (state == null)
        {
            Debug.LogError(string.Format("Cannot crossfade to state with name {0} because there is no state with that name", name));
            return false;
        }

        if (time == 0f)
            return Play(state.index);

        return Crossfade(state.index, time);
    }

    public bool CrossfadeQueued(string name, float time, QueueMode queueMode)
    {
        StateInfo state = m_States.FindState(name);
        if (state == null)
        {
            Debug.LogError(string.Format("Cannot queue crossfade to state with name {0} because there is no state with that name", name));
            return false;
        }

        return CrossfadeQueued(state.index, time, queueMode);
    }

    private bool CrossfadeQueued(int index, float time, QueueMode queueMode)
    {
        // 複製一個排進queue
        StateInfo newState = CloneState(index);

        if (queueMode == QueueMode.PlayNow)
        {
            Crossfade(newState.index, time);
            return true;
        }

        m_StateQueue.AddLast(new QueuedState(StateInfoToHandle(newState), time));
        return true;
    }

    /// <summary>
    /// 在time時間內，將指定state以targetWeight混合進來。
    /// 此方法與CrossFade不同之處是Blend不會把其他state的weight改成0
    /// </summary>
    private bool Blend(int index, float targetWeight, float time)
    {
        StateInfo state = m_States[index];
        if (state.enabled == false)
            m_States.EnableState(index);

        if (time == 0f)
        {
            state.ForceWeight(targetWeight);
        }
        else
        {
            SetupLerp(state, targetWeight, time);
        }

        return true;
    }

    /// <summary>
    /// 在time時間內，將指定state以targetWeight混合進來。
    /// 此方法與CrossFade不同之處是Blend不會把其他state的weight改成0
    /// </summary>
    public bool Blend(string name, float targetWeight, float time)
    {
        StateInfo state = m_States.FindState(name);
        if (state == null)
        {
            Debug.LogError(string.Format("Cannot blend state with name {0} because there is no state with that name", name));
            return false;
        }

        return Blend(state.index, targetWeight, time);
    }

    public override void OnGraphStop(Playable playable)
    {
        //if the playable is not valid, then we are destroying, and our children won't be valid either
        if (!self.IsValid())
            return;

        for (int i = 0; i < m_States.Count; i++)
        {
            StateInfo state = m_States[i];
            if (state == null)
                continue;

            if (state.fadeSpeed == 0f && state.targetWeight == 0f)
            {
                Playable input = m_Mixer.GetInput(state.index);
                if (!input.Equals(Playable.Null))
                {
                    input.ResetTime(0f);
                }
            }
        }
    }

    /// <summary>
    /// Check if any state is playing. if not, this playable will goes to done state and call onDone() callback.
    /// </summary>
    private void UpdateDoneStatus()
    {
        if (!m_States.AnyStatePlaying())
        {
            bool wasDone = playable.IsDone();
            playable.SetDone(true);
            if (!wasDone && onDone != null)
            {
                onDone();
            }
        }

    }

    /// <summary>
    /// 清除已經Stop且未被刪除的clone states
    /// </summary>
    private void CleanClonedStates()
    {
        for (int i = m_States.Count-1; i >= 0; i--)
        {
            StateInfo state = m_States[i];
            if (state == null)
                continue;

            if (state.isReadyForCleanup)
            {
                Playable toDestroy = m_Mixer.GetInput(state.index);
                graph.Disconnect(m_Mixer, state.index);
                graph.DestroyPlayable(toDestroy);
                m_States.RemoveState(i);
            }
        }
    }

    /// <summary>
    /// 把指定state的playable與graph的聯繫斷開(但state仍存在)
    /// </summary>
    /// <param name="index"></param>
    private void DisconnectInput(int index)
    {
        if (keepStoppedPlayablesConnected)
        {
            m_States[index].Pause();
        }
        graph.Disconnect(m_Mixer, index);
    }

    /// <summary>
    /// 把指定state的playable連回graph
    /// </summary>
    /// <param name="index"></param>
    private void ConnectInput(int index)
    {
        StateInfo state = m_States[index];
        graph.Connect(state.playable, 0, m_Mixer, state.index);
    }

    /// <summary>
    /// 更新所有States的Playable資料。
    /// 包含Fading, EnabledDirty, WrapMode.Once, 和 WeightDirty。
    /// </summary>
    /// <param name="deltaTime"></param>
    private void UpdateStates(float deltaTime)
    {
        bool mustUpdateWeights = false;
        float totalWeight = 0f;
        for (int i = 0; i < m_States.Count; i++)
        {
            StateInfo state = m_States[i];

            //Skip deleted states
            if (state == null)
            {
                continue;
            }

            //Update crossfade weight
            if (state.fading)
            {
                state.SetWeight(Mathf.MoveTowards(state.weight, state.targetWeight, state.fadeSpeed * deltaTime));
                if (Mathf.Approximately(state.weight, state.targetWeight))
                {
                    state.ForceWeight(state.targetWeight);
                    if (state.weight == 0f)
                    {
                        state.Stop();
                    }
                }
            }

            if (state.enabledDirty)
            {
                if (state.enabled)
                    state.Play();
                else
                    state.Pause();

                if (!keepStoppedPlayablesConnected)
                {
                    Playable input = m_Mixer.GetInput(i);
                    //if state is disabled but the corresponding input is connected, disconnect it
                    if (input.IsValid() && !state.enabled)
                    {
                        DisconnectInput(i);
                    }
                    else if (state.enabled && !input.IsValid())
                    {
                        ConnectInput(state.index);
                    }
                }
            }

            // 檢查單次播放是否完成
            if (state.enabled && state.wrapMode == WrapMode.Once)
            {
                bool stateIsDone = state.isDone;
                float speed = state.speed;
                float time = state.GetTime();
                float duration = state.playableDuration;

                stateIsDone |= speed < 0f && time < 0f; // 倒著播完
                stateIsDone |= speed >= 0f && time >= duration; // 順著播完
                if (stateIsDone)
                {
                    state.Stop();
                    state.Disable();
                    if (!keepStoppedPlayablesConnected)
                        DisconnectInput(state.index);

                }
            }

            totalWeight += state.weight;
            // 如果有任何一個state被SetWeight()過
            if (state.weightDirty)
            { // 待會會更新所有input的weight
                mustUpdateWeights = true;
            }
            state.ResetDirtyFlags();
        }

        // 更新所有input的weight
        if (mustUpdateWeights)
        {
            bool hasAnyWeight = totalWeight > 0.0f;
            for (int i = 0; i < m_States.Count; i++)
            {
                StateInfo state = m_States[i];
                if (state == null)
                    continue;

                // Input weight的算法與State.weight不同，它會把state weight給normalized至[0, 1]
                float weight = hasAnyWeight ? state.weight / totalWeight : 0.0f;
                m_Mixer.SetInputWeight(state.index, weight);
            }
        }
    }

    /// <summary>
    /// 取得目前所有播放中的state的最大剩餘時間
    /// </summary>
    /// <returns></returns>
    private float CalculateQueueTimes()
    {
        float longestTime = -1f;

        for (int i = 0; i < m_States.Count; i++)
        {
            StateInfo state = m_States[i];
            //Skip deleted states
            if (state == null || !state.enabled || !state.playable.IsValid())
                continue;

            if (state.wrapMode == WrapMode.Loop)
            {
                return Mathf.Infinity;
            }

            float speed = state.speed;
            float stateTime = m_States.GetStateTime(state.index);
            float remainingTime;
            if (speed > 0)
            {
                remainingTime = (state.clip.length - stateTime) / speed;
            }
            else if(speed < 0)
            {
                remainingTime = (stateTime) / speed;
            }
            else
            {  // speed == 0
                remainingTime = Mathf.Infinity;
            }

            // Maximum
            if (remainingTime > longestTime)
            {
                longestTime = remainingTime;
            }
        }

        return longestTime;
    }

    private void ClearQueuedStates()
    {
        using (var it = m_StateQueue.GetEnumerator())
        {
            while (it.MoveNext())
            {
                QueuedState queuedState = it.Current;
                m_States.StopState(queuedState.state.index, true);
            }
        }
        m_StateQueue.Clear();
    }

    /// <summary>
    /// 更新Queue，檢查是否需要轉換到下一個state(fading)
    /// </summary>
    private void UpdateQueuedStates()
    {
        var it = m_StateQueue.First;
        while(it != null)
        {
            // 找當前state中最長的remainingTime
            float remainingTime = CalculateQueueTimes();

            QueuedState queuedState = it.Value;

            // 如果剩餘的時間小於fadeTime則直接fade過去。
            if (queuedState.fadeTime >= remainingTime)
            {
                Crossfade(queuedState.state.index, queuedState.fadeTime);
                m_StateQueue.RemoveFirst();
                it = m_StateQueue.First; // 繼續檢查下一個是否該進入fade
            } else {
                break; // 如果沒有就跳出
            }
        }
    }

    /// <summary>
    /// 將所有states的標記為invalid time，使得下次取得time時要重新跑GetTime()
    /// </summary>
    void InvalidateStateTimes()
    {
        int count = m_States.Count;
        for (int i = 0; i < count; i++)
        {
            StateInfo state = m_States[i];
            if (state == null)
                continue;

            state.InvalidateTime();
        }
    }

    public override void PrepareFrame(Playable owner, FrameData data)
    {
        InvalidateStateTimes();

        UpdateQueuedStates();

        UpdateStates(data.deltaTime);

        //Once everything is calculated, update done status
        UpdateDoneStatus();

        CleanClonedStates();
    }

    public bool ValidateInput(int index, Playable input)
    {
        if (!ValidateIndex(index))
            return false;

        StateInfo state = m_States[index];
        if (state == null || !state.playable.IsValid() || state.playable.GetHandle() != input.GetHandle())
            return false;

        return true;
    }

    public bool ValidateIndex(int index)
    {
        return index >= 0 && index < m_States.Count;
    }
}