using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using System;

public partial class SimpleAnimationPlayable : PlayableBehaviour
{
    /// <summary>
    /// 用於標記目前SimpleAnimationPlayable的版本，每當clip被添加或刪除時m_StatesVersion都會+1。
    /// SimpleAnimationPlayable.GetStates()會得到StateEnumerable物件，
    /// 而該物件的GetEnumerator()方法所得到的StateEnumerator在被創建時會將這個版本號記錄下來，
    /// 之後每當操作StateEnumerator時他都會執行IsValid()檢查當初創建的版本號與SimpleAnimationPlayable目前的版本是否一致，
    /// 如果不一致則代表playable內的clip有更動，因此那個StateEnumerator是invalid的而拋出例外。
    /// </summary>
    private int m_StatesVersion = 0;

    /// <summary>
    /// State有變動，因此上升版本。
    /// </summary>
    private void InvalidateStates() { m_StatesVersion++; }

    /// <summary>
    /// SimpeAnimationPlayable.GetStates()的IEnumerable形式，之後可以透過GetEnumerator()取得IEnumerator以方便做遍歷。
    /// </summary>
    private class StateEnumerable: IEnumerable<IState>
    {
        private SimpleAnimationPlayable m_Owner;
        public StateEnumerable(SimpleAnimationPlayable owner)
        {
            m_Owner = owner;
        }

        public IEnumerator<IState> GetEnumerator()
        {
            return new StateEnumerator(m_Owner);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return new StateEnumerator(m_Owner);
        }

        class StateEnumerator : IEnumerator<IState>
        {
            private int m_Index = -1;
            private int m_Version;
            private SimpleAnimationPlayable m_Owner;
            public StateEnumerator(SimpleAnimationPlayable owner)
            {
                m_Owner = owner;
                m_Version = m_Owner.m_StatesVersion;
                Reset();
            }

            /// <summary>
            /// 檢查當初創建的SimpleAnimationPlayable版本號與目前的版本號是否一致，
            /// 如果不一致則代表playable內的clip有更動，所以會成為invalid的Enumerator。
            /// </summary>
            private bool IsValid() { return m_Owner != null && m_Version == m_Owner.m_StatesVersion; }

            IState GetCurrentHandle(int index)
            {
                if (!IsValid())
                    throw new InvalidOperationException("The collection has been modified, this Enumerator is invalid");

                if (index < 0 || index >= m_Owner.m_States.Count)
                    throw new InvalidOperationException("Enumerator is invalid");

                StateInfo state = m_Owner.m_States[index];
                if (state == null)
                    throw new InvalidOperationException("Enumerator is invalid");

                return new StateHandle(m_Owner, state.index, state.playable);
            }

            object IEnumerator.Current { get { return GetCurrentHandle(m_Index); } }

            IState IEnumerator<IState>.Current { get { return GetCurrentHandle(m_Index); } }

            public void Dispose() { }

            public bool MoveNext()
            {
                if (!IsValid())
                    throw new InvalidOperationException("The collection has been modified, this Enumerator is invalid");

                do
                { m_Index++; } while (m_Index < m_Owner.m_States.Count && m_Owner.m_States[m_Index] == null);

                return m_Index < m_Owner.m_States.Count;
            }

            public void Reset()
            {
                if (!IsValid())
                    throw new InvalidOperationException("The collection has been modified, this Enumerator is invalid");
                m_Index = -1;
            }
        }
    }
    
    /// <summary>
    /// State的介面，提供類似於AnimatorStateInfo的資訊
    /// </summary>
    public interface IState
    {
        bool IsValid();

        bool enabled { get; set; }

        float time { get; set; }

        float normalizedTime { get; set; }

        float speed { get; set; }

        string name { get; set; }

        float weight { get; set; }

        float length { get; }

        AnimationClip clip { get; }

        WrapMode wrapMode { get; }
    }

    /// <summary>
    /// 實作IState的一層Proxy，內部主要是由 SimpleAnimationPlayable.StateInfo 組成。
    /// 是用來給予SimpleAnimationPlayable以外的類別使用StateInfo
    /// </summary>
    public class StateHandle : IState
    {
        public StateHandle(SimpleAnimationPlayable s, int index, Playable target)
        {
            m_Parent = s;
            m_Index = index;
            m_Target = target;
        }

        public bool IsValid()
        {
            return m_Parent.ValidateInput(m_Index, m_Target);
        }

        public bool enabled
        {
            get
            {
                if (!IsValid())
                    throw new System.InvalidOperationException("This StateHandle is not valid");
                return m_Parent.m_States[m_Index].enabled;
            }

            set
            {
                if (!IsValid())
                    throw new System.InvalidOperationException("This StateHandle is not valid");
                if (value)
                    m_Parent.m_States.EnableState(m_Index);
                else
                    m_Parent.m_States.DisableState(m_Index);

            }
        }

        public float time
        {
            get
            {
                if (!IsValid())
                    throw new System.InvalidOperationException("This StateHandle is not valid");
                return m_Parent.m_States.GetStateTime(m_Index);
            }
            set
            {
                if (!IsValid())
                    throw new System.InvalidOperationException("This StateHandle is not valid");
                m_Parent.m_States.SetStateTime(m_Index, value);
            }
        }

        public float normalizedTime
        {
            get
            {
                if (!IsValid())
                    throw new System.InvalidOperationException("This StateHandle is not valid");

                float length = m_Parent.m_States.GetClipLength(m_Index);
                if (length == 0f)
                    length = 1f;

                return m_Parent.m_States.GetStateTime(m_Index) / length;
            }
            set
            {
                if (!IsValid())
                    throw new System.InvalidOperationException("This StateHandle is not valid");

                float length = m_Parent.m_States.GetClipLength(m_Index);
                if (length == 0f)
                    length = 1f;

                m_Parent.m_States.SetStateTime(m_Index, value *= length);
            }
        }

        public float speed
        {
            get
            {
                if (!IsValid())
                    throw new System.InvalidOperationException("This StateHandle is not valid");
                return m_Parent.m_States.GetStateSpeed(m_Index);
            }
            set
            {
                if (!IsValid())
                    throw new System.InvalidOperationException("This StateHandle is not valid");
                m_Parent.m_States.SetStateSpeed(m_Index, value);
            }
        }

        public string name
        {
            get
            {
                if (!IsValid())
                    throw new System.InvalidOperationException("This StateHandle is not valid");
                return m_Parent.m_States.GetStateName(m_Index);
            }
            set
            {
                if (!IsValid())
                    throw new System.InvalidOperationException("This StateHandle is not valid");
                if (value == null)
                    throw new System.ArgumentNullException("A null string is not a valid name");
                m_Parent.m_States.SetStateName(m_Index, value);
            }
        }

        public float weight
        {
            get
            {
                if (!IsValid())
                    throw new System.InvalidOperationException("This StateHandle is not valid");
                return m_Parent.m_States[m_Index].weight;
            }
            set
            {
                if (!IsValid())
                    throw new System.InvalidOperationException("This StateHandle is not valid");
                if (value < 0)
                    throw new System.ArgumentException("Weights cannot be negative");

                m_Parent.m_States.SetInputWeight(m_Index, value);
            }
        }

        public float length
        {
            get
            {
                if (!IsValid())
                    throw new System.InvalidOperationException("This StateHandle is not valid");
                return m_Parent.m_States.GetStateLength(m_Index);
            }
        }

        public AnimationClip clip
        {
            get
            {
                if (!IsValid())
                    throw new System.InvalidOperationException("This StateHandle is not valid");
                return m_Parent.m_States.GetStateClip(m_Index);
            }
        }

        public WrapMode wrapMode
        {
            get
            {
                if (!IsValid())
                    throw new System.InvalidOperationException("This StateHandle is not valid");
                return m_Parent.m_States.GetStateWrapMode(m_Index);
            }
        }

        public int index { get { return m_Index; } }

        private SimpleAnimationPlayable m_Parent;
        private int m_Index;
        private Playable m_Target;
    }

    private class StateInfo
    {
        public void Initialize(string name, AnimationClip clip, WrapMode wrapMode)
        {
            m_StateName = name;
            m_Clip = clip;
            m_WrapMode = wrapMode;
        }

        /// <summary>
        /// 取得playable的Time屬性
        /// </summary>
        /// <returns></returns>
        public float GetTime()
        {
            // lazy getter: 如果在一次更新中已經取得過time了，則無須再次GetTime()。
            // 當State更新時(PrepareFrame()時)，會呼叫InvalidateTime()來使得m_TimeIsUpToDate=false
            if (m_TimeIsUpToDate)
                return m_Time;

            m_Time = (float)m_Playable.GetTime();
            m_TimeIsUpToDate = true;
            return m_Time;
        }

        public void SetTime(float newTime)
        {
            m_Time = newTime;
            m_Playable.ResetTime(m_Time);
            m_Playable.SetDone(m_Time >= m_Playable.GetDuration());
        }

        public void Enable()
        {
            if (m_Enabled)
                return;

            m_EnabledDirty = true;
            m_Enabled = true;
        }

        public void Disable()
        {
            if (m_Enabled == false)
                return;

            m_EnabledDirty = true;
            m_Enabled = false;
        }

        public void Pause()
        {
            m_Playable.Pause();
        }

        public void Play()
        {
            m_Playable.Play();
        }

        /// <summary>
        /// 停止播放此State
        /// </summary>
        public void Stop()
        {
            m_FadeSpeed = 0f;
            ForceWeight(0.0f);
            Disable();
            SetTime(0.0f);
            m_Playable.SetDone(false);
            if (isClone)
            {
                m_ReadyForCleanup = true;
            }
        }

        /// <summary>
        /// 設定State的weight，並重置Fading相關參數。
        /// </summary>
        public void ForceWeight(float weight)
        {
           m_TargetWeight = weight;
           m_Fading = false;
           m_FadeSpeed = 0f;
           SetWeight(weight);
        }

        /// <summary>
        /// 設定State的weight並記錄dirty。
        /// 這個方法並不會即時更新Playable的weight。
        /// </summary>
        public void SetWeight(float weight)
        {
            m_Weight = weight;
            m_WeightDirty = true;
        }

        public void FadeTo(float weight, float speed)
        {
            m_Fading = Mathf.Abs(speed) > 0f;
            m_FadeSpeed = speed;
            m_TargetWeight = weight;
        }

        /// <summary>
        /// 從graph中刪除這個state的playable
        /// </summary>
        public void DestroyPlayable()
        {
            if (m_Playable.IsValid())
            {
                m_Playable.GetGraph().DestroySubgraph(m_Playable);
            }
        }

        public void SetAsCloneOf(StateHandle handle)
        {
            m_ParentState = handle;
            m_IsClone = true;
        }

        /// <summary>
        /// 設定此state是否啟用(能否被播放)
        /// </summary>
        public bool enabled
        {
            get { return m_Enabled; }
        }

        private bool m_Enabled;

        public int index
        {
            get { return m_Index; }
            set
            {
                Debug.Assert(m_Index == 0, "Should never reassign Index");
                m_Index = value;
            }
        }

        private int m_Index;

        public string stateName
        {
            get { return m_StateName; }
            set { m_StateName = value; }
        }

        private string m_StateName;

        public bool fading
        {
            get { return m_Fading; }
        }

        private bool m_Fading;


        private float m_Time;

        public float targetWeight
        {
            get { return m_TargetWeight; }
        }

        private float m_TargetWeight;

        public float weight
        {
            get { return m_Weight; }
        }

        float m_Weight;

        public float fadeSpeed
        {
            get { return m_FadeSpeed; }
        }

        float m_FadeSpeed;

        public float speed
        {
            get { return (float)m_Playable.GetSpeed(); }
            set { m_Playable.SetSpeed(value); }
        }

        public float playableDuration
        {
            get { return (float)m_Playable.GetDuration(); }
        }

        public AnimationClip clip
        {
            get { return m_Clip; }
        }

        private AnimationClip m_Clip;

        public void SetPlayable(Playable playable)
        {
            m_Playable = playable;
        }

        public bool isDone { get { return m_Playable.IsDone(); } }

        public Playable playable
        {
            get { return m_Playable; }
        }

        private Playable m_Playable;

        public WrapMode wrapMode
        {
            get { return m_WrapMode; }
        }

        private WrapMode m_WrapMode;

        /// <summary>
        /// This state is a clone of other state.
        /// </summary>
        public bool isClone
        {
            get { return m_IsClone; }
        }

        private bool m_IsClone;

        /// <summary>
        /// 如果這個State是一個clone且它已經被Stop()了，則這個flag就會為true。
        /// </summary>
        public bool isReadyForCleanup
        {
            get { return m_ReadyForCleanup; }
        }

        private bool m_ReadyForCleanup;

        /// <summary>
        /// The original state which this clone as.
        /// </summary>
        public StateHandle parentState
        {
            get { return m_ParentState; }
        }

        private StateHandle m_ParentState;

        public bool enabledDirty { get { return m_EnabledDirty; } }
        public bool weightDirty { get { return m_WeightDirty; } }

        public void ResetDirtyFlags()
        { 
            m_EnabledDirty = false;
            m_WeightDirty = false;
        }

        private bool m_WeightDirty;
        private bool m_EnabledDirty;

        /// <summary>
        /// Indicate the m_Time is invalid now.
        /// </summary>
        public void InvalidateTime() { m_TimeIsUpToDate = false; }
        private bool m_TimeIsUpToDate;
    }

    private StateHandle StateInfoToHandle(StateInfo info)
    {
        return new StateHandle(this, info.index, info.playable);
    }

    private class StateManagement
    {
        private List<StateInfo> m_States;

        public int Count { get { return m_Count; } }

        private int m_Count;

        public StateInfo this[int i]
        {
            get
            {
                return m_States[i];
            }
        }

        public StateManagement()
        {
            m_States = new List<StateInfo>();
        }

        /// <summary>
        /// 生成一個新的StateInfo並試圖找m_States內為null的位置插入，若沒有則Add至最後。
        /// </summary>
        /// <returns></returns>
        public StateInfo InsertState()
        {
            StateInfo state = new StateInfo();

            int firstAvailable = m_States.FindIndex(s => s == null);
            if (firstAvailable == -1)
            {
                firstAvailable = m_States.Count;
                m_States.Add(state);
            }
            else
            {
                m_States.Insert(firstAvailable, state);
            }

            state.index = firstAvailable;
            m_Count++;
            return state;
        }
        public bool AnyStatePlaying()
        {
            return m_States.FindIndex(s => s != null && s.enabled) != -1;
        }

        /// <summary>
        /// 從Graph中完全移除指定的State
        /// </summary>
        /// <param name="index"></param>
        public void RemoveState(int index)
        {
            StateInfo removed = m_States[index];
            m_States[index] = null;
            removed.DestroyPlayable();
            m_Count = m_States.Count;
        }

        public bool RemoveClip(AnimationClip clip)
        {
            bool removed = false;
            for (int i = 0; i < m_States.Count; i++)
            {
                StateInfo state = m_States[i];
                if (state != null &&state.clip == clip)
                {
                    RemoveState(i);
                    removed = true;
                }
            }
            return removed;
        }

        /// <summary>
        /// 透過List<T>.FindIndex()搜尋名稱符合的state
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public StateInfo FindState(string name)
        {
            int index = m_States.FindIndex(s => s != null && s.stateName == name);
            if (index == -1)
                return null;

            return m_States[index];
        }

        public void EnableState(int index)
        {
            StateInfo state = m_States[index];
            state.Enable();
        }

        public void DisableState(int index)
        {
            StateInfo state = m_States[index];
            state.Disable();
        }

        public void SetInputWeight(int index, float weight)
        {
            StateInfo state = m_States[index];
            state.SetWeight(weight);
           
        }

        public void SetStateTime(int index, float time)
        {
            StateInfo state = m_States[index];
            state.SetTime(time);
        }

        public float GetStateTime(int index)
        {
            StateInfo state = m_States[index];
            return state.GetTime();
        }

        public bool IsCloneOf(int potentialCloneIndex, int originalIndex)
        {
            StateInfo potentialClone = m_States[potentialCloneIndex];
            return potentialClone.isClone && potentialClone.parentState.index == originalIndex;
        }

        public float GetStateSpeed(int index)
        {
            return m_States[index].speed;
        }
        public void SetStateSpeed(int index, float value)
        {
            m_States[index].speed = value;
        }

        public float GetInputWeight(int index)
        {
            return m_States[index].weight;
        }

        public float GetStateLength(int index)
        {
            AnimationClip clip = m_States[index].clip;
            if (clip == null)
                return 0f;
            float speed = m_States[index].speed;
            if (speed == 0f)
                return Mathf.Infinity;

            return clip.length / speed;
        }

        public float GetClipLength(int index)
        {
            AnimationClip clip = m_States[index].clip;
            if (clip == null)
                return 0f;

            return clip.length;
        }

        public float GetStatePlayableDuration(int index)
        {
            return m_States[index].playableDuration;
        }

        public AnimationClip GetStateClip(int index)
        {
            return m_States[index].clip;
        }

        public WrapMode GetStateWrapMode(int index)
        {
            return m_States[index].wrapMode;
        }

        public string GetStateName(int index)
        {
            return m_States[index].stateName;
        }

        public void SetStateName(int index, string name)
        {
            m_States[index].stateName = name;
        }

        /// <summary>
        /// 停止播放State，如果cleanup為true則會將State從graph中移除
        /// </summary>
        /// <param name="index"></param>
        /// <param name="cleanup"></param>
        public void StopState(int index, bool cleanup)
        {
            if (cleanup)
            {
                RemoveState(index);
            }
            else
            {
                m_States[index].Stop();
            }
        }

    }

    private struct QueuedState
    {
        public QueuedState(StateHandle s, float t)
        {
            state = s;
            fadeTime = t;
        }

        public StateHandle state;
        /// <summary>
        /// How much time the state fading takes.
        /// </summary>
        public float fadeTime;
    }

}
