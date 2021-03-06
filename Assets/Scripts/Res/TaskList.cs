// 任务队列

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 任务接口
public abstract class ITask
{
	// 是否已经好了
	public bool IsDone {
		get
		{
			return (mResult != 0);
		}
	}

	// 结果
	public int Result {
		get
		{
			return mResult;
		}
		set
		{
			mResult = value;
		}
	}

	public bool IsDoing
	{
		get {
			return mResult == 0;
		}
	}

	public bool IsOk
	{
		get {
			return mResult > 0;
		}
	}

	public bool IsFail
	{
		get {
			return mResult < 0;
		}
	}

	public void AddResultEvent(Action<ITask> evt)
	{
		if (OnResult == null)
			OnResult = evt;
		else
			OnResult += evt;
	}

	// 执行回调
	public Action<ITask> OnResult {
		get;
		protected set;
	}

	// 用户数据
	public System.Object UserData {
		get;
		set;
	}

	// 设置拥有者
	public TaskList _Owner
	{
		get {
			return mOwner;
		}

		set {
			mOwner = value;
		}
	}

	// 处理
	public abstract void Process();
	public virtual void Release()
	{
	}

	protected void TaskOk()
	{
		mResult = 1;
	}

	protected void TaskFail()
	{
		mResult = -1;
	}

	private int mResult = 0;
	private TaskList mOwner = null;
}

// WWW 文件读取任务
public class WWWFileLoadTask: ITask
{
	// 注意：必须是WWW支持的文件名 PC上需要加 file:///
	public WWWFileLoadTask(string wwwFileName)
	{
		if (string.IsNullOrEmpty(wwwFileName)) {
			TaskFail();
			return;
		}

		mWWWFileName = wwwFileName;
	}
	
	// 传入为普通文件名(推荐使用这个函数)
	public static WWWFileLoadTask LoadFileName(string fileName)
	{
		string wwwFileName = ConvertToWWWFileName(fileName);
		WWWFileLoadTask ret = new WWWFileLoadTask(wwwFileName);
		return ret;
	}
	
	// 读取StreamingAssets目录下的文件，只需要相对于StreamingAssets的路径即可(推荐使用这个函数)
	public static WWWFileLoadTask LoadFileAtStreamingAssetsPath(string fileName, bool usePlatform)
	{
		fileName = GetStreamingAssetsPath(usePlatform) + "/" + fileName;
		WWWFileLoadTask ret = LoadFileName(fileName);
		return ret;
	}

	public static string GetStreamingAssetsPath(bool usePlatform, bool isUseABCreateFromFile = false)
	{
		string ret = string.Empty;
		switch (Application.platform)
		{
			case RuntimePlatform.OSXPlayer:
			{
				ret = Application.streamingAssetsPath;
				if (usePlatform)
					ret += "/Mac";
				break;
			}

			case RuntimePlatform.OSXEditor:
			{
				ret = "Assets/StreamingAssets";
				if (usePlatform)
					ret += "/Mac";
				break;
			}

			case RuntimePlatform.WindowsPlayer:
			{
				ret = Application.streamingAssetsPath;
				if (usePlatform)
					ret += "/Windows";
				break;
			}

			case RuntimePlatform.WindowsEditor:
			{
				ret = "Assets/StreamingAssets";
				if (usePlatform)
					ret += "/Windows";
				break;
			}
			case RuntimePlatform.Android:
			{
				if (isUseABCreateFromFile)
					ret = Application.dataPath + "!assets";
				else
					ret = Application.streamingAssetsPath;
				if (usePlatform)
					ret += "/Android";
				break;
			}
			case RuntimePlatform.IPhonePlayer:
			{
				ret = Application.streamingAssetsPath;
				if (usePlatform)
					ret += "/IOS";
				break;
			}
			default:
				ret = Application.streamingAssetsPath;
				break;
		}
		
		return ret;
	}
	
	// 普通文件名转WWW文件名
	public static string ConvertToWWWFileName(string fileName)
	{
		if (string.IsNullOrEmpty(fileName))
			return string.Empty;
		string ret = System.IO.Path.GetFullPath(fileName);
		if (string.IsNullOrEmpty(ret))
			return string.Empty;
		switch (Application.platform)
		{
			case RuntimePlatform.OSXEditor:
				ret = "file:///" + ret;
				break;
			case RuntimePlatform.WindowsEditor:
				ret = "file:///" + ret; 
				break;
			case RuntimePlatform.OSXPlayer:
				ret = "file:///" + ret; 
				break;
			case RuntimePlatform.WindowsPlayer:
				ret = "file:///" + ret; 
				break;
			case RuntimePlatform.Android:
				ret = ret.Replace("/jar:file:/", "jar:file:///");
				break;
		}
		return ret;
	}

	public override void Release()
	{
		base.Release ();

		if (mLoader != null) {
			mLoader.Dispose();
			mLoader = null;
		}

		mByteData = null;
		mBundle = null;
		mProgress = 0;
		mWWWFileName = string.Empty;
	}

	public override void Process()
	{
		if (mLoader == null) {
			mLoader = new WWW (mWWWFileName);
		}

		if (mLoader == null) {
			TaskFail();
			return;
		}

		if (mLoader.isDone) {
			if (mLoader.assetBundle != null) {
				mProgress = 1.0f;
				TaskOk ();
				mBundle = mLoader.assetBundle;
			} else
			if ((mLoader.bytes != null) && (mLoader.bytes.Length > 0)) {
				mProgress = 1.0f;
				TaskOk ();
				mByteData = mLoader.bytes;
			} else
				TaskFail ();

			mLoader.Dispose ();
			mLoader = null;
		} else {
			mProgress = mLoader.progress;
		}

		if (OnProcess != null)
			OnProcess (this);
	}

	public byte[] ByteData
	{
		get 
		{
			return mByteData;
		}
	}

	public AssetBundle Bundle
	{
		get {
			return mBundle;
		}
	}

	public float Progress
	{
		get {
			return mProgress;
		}
	}

	public Action<WWWFileLoadTask> OnProcess {
		get;
		set;
	}

	private WWW mLoader = null;
	private byte[] mByteData = null;
	private AssetBundle mBundle = null;
	private string mWWWFileName = string.Empty;
	private float mProgress = 0;
}

// 加载场景任务
public class LevelLoadTask: ITask
{
	// 场景名 是否增加方式 是否是异步模式  onProgress(float progress, int result)
	// result: 0 表示进行中 1 表示加载完成 -1表示加载失败
	public LevelLoadTask(string sceneName, bool isAdd, bool isAsync, Action<float, int> onProcess)
	{
		if (string.IsNullOrEmpty (sceneName)) {
			TaskFail();
			return;
		}

		mSceneName = sceneName;
		mIsAdd = isAdd;
		mIsAsync = isAsync;
		mOnProgress = onProcess;
	}

	public override void Process()
	{
		// 同步
		if (!mIsAsync) {
            bool isResult = Application.CanStreamedLevelBeLoaded(mSceneName);

            if (isResult)
            {
                if (mIsAdd)
                    Application.LoadLevelAdditive(mSceneName);
                else
                    Application.LoadLevel(mSceneName);
            }

			
			if (isResult)
			{
				TaskOk();
			}
			else
				TaskFail();

			if (mOnProgress != null)
			{
				if (isResult)
					mOnProgress(1.0f, 1);
				else
					mOnProgress(0, -1);
			}
			
			return;
		}

		if (mOpr == null) {
			// 异步
			if (mIsAdd)
				mOpr = Application.LoadLevelAdditiveAsync (mSceneName);
			else
				mOpr = Application.LoadLevelAsync (mSceneName);
		}

		if (mOpr == null) {
			TaskFail();
			if (mOnProgress != null)
				mOnProgress(0, -1);
			return;
		}

		if (mOpr.isDone) {
			TaskOk();
			if (mOnProgress != null)
				mOnProgress(1.0f, 1);
		} else {
			if (mOnProgress != null)
				mOnProgress(mOpr.progress, 0);
		}

	}

	public string SceneName
	{
		get {
			return mSceneName;
		}
	}

	private string mSceneName = string.Empty;
	private bool mIsAdd = false;
	private bool mIsAsync = false;
	private AsyncOperation mOpr = null;
	private Action<float, int> mOnProgress = null;
}

// 任务列表(为了保证顺序执行)
public class TaskList
{
	// 保证不要加重复的
	public void AddTask(ITask task, bool isOwner)
	{
		if (task == null)
			return;

		mTaskList.AddLast (task);
		if (isOwner)
			task._Owner = this;
	}

	// 保证不要加重复的
	public void AddTask(LinkedListNode<ITask> node, bool isOwner)
	{
		if ((node == null) || (node.Value == null))
			return;
		mTaskList.AddLast (node);
		if (isOwner)
			node.Value._Owner = this;
	}

	public void Process()
	{
		LinkedListNode<ITask> node = mTaskList.First;
		if ((node != null) && (node.Value != null)) {
			if (node.Value.IsDone)
			{
				TaskEnd(node.Value);
				mTaskList.RemoveFirst();
				return;
			}

			TaskProcess(node.Value);

			if (node.Value.IsDone)
			{
				TaskEnd(node.Value);
				mTaskList.RemoveFirst();
			}
		}
	}

	public bool IsEmpty
	{
		get{
			return mTaskList.Count <= 0;
		}
	}

	// 慎用
	public void Clear()
	{

		var node = mTaskList.First;
		while (node != null) {
			var next = node.Next;
			if (node.Value != null)
				node.Value.Release();
			node = next;
		}

		mTaskList.Clear ();
	}

	private void TaskEnd(ITask task)
	{
		if ((task == null) || (!task.IsDone))
			return;
		if ((task._Owner == this) && (task.OnResult != null))
			task.OnResult (task);
	}

	private void TaskProcess(ITask task)
	{
		if (task == null)
			return;

		if (task._Owner == this)
			task.Process ();
	}
	
	// 任务必须是顺序执行
	private LinkedList<ITask> mTaskList = new LinkedList<ITask>();
}