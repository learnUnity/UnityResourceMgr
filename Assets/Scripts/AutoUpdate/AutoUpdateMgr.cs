﻿using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using Utils;
using NsHttpClient;

// 更新模块
namespace AutoUpdate
{
	public enum AutoUpdateState
	{
		// 准备阶段
		auPrepare,
		// 请求检查版本
		auCheckVersionReq,
		// 请求资源列表
		auGetResListReq,
		// 获得某个更新文件
		auUpdateFileProcess,
		// 完成
		auFinished,
		auEnd
	}

	public enum AutoUpdateErrorType
	{
		auError_None = 0,
		auError_NoGetVersion = 1,
		auError_NoGetFileList = 2,
		auError_FileDown = 3
	}

	public class AutoUpdateMgr: Singleton<AutoUpdateMgr>
	{
		// 是否需要更新Version.txt
		public bool IsVersionTxtNoUpdate()
		{
			int ret = string.Compare(LocalResVersion, CurrServeResrVersion, StringComparison.CurrentCultureIgnoreCase);
			return ret >= 0;
		}

		public bool IsVersionNoUpdate()
		{
			return IsVersionTxtNoUpdate() &&
				   string.Compare(LocalFileListContentMd5, ServerFileListContentMd5,  StringComparison.CurrentCultureIgnoreCase) == 0;
		}

		public AutoUpdateMgr()
		{
			DownProcess = 0;
			m_WritePath = FilePathMgr.Instance.WritePath;
			m_StateMgr = new AutoUpdateStateMgr(this);
			m_UpdateFile = new AutoUpdateCfgFile();
			if (!string.IsNullOrEmpty(m_WritePath))
			{
				m_UpdateFile.SaveFileName = string.Format("{0}/{1}", m_WritePath, _cUpdateTxt);
			}
			RegisterStates();
		}

		public string WritePath
		{
			get
			{
				return m_WritePath;
			}
		}

		private void RegisterStates()
		{
			AutoUpdateStateMgr.Register(AutoUpdateState.auPrepare,
			                            new AutoUpdatePrepareState());

			AutoUpdateStateMgr.Register(AutoUpdateState.auCheckVersionReq, 
			                            new AutoUpdateCheckVersionState());
			AutoUpdateStateMgr.Register(AutoUpdateState.auGetResListReq,
			                            new AutoUpdateFileListState());

			AutoUpdateStateMgr.Register(AutoUpdateState.auUpdateFileProcess, 
			                            new AutoUpdateFileDownloadState());

			AutoUpdateStateMgr.Register(AutoUpdateState.auFinished,
			                            new AutoUpdateFinishState());

			AutoUpdateStateMgr.Register(AutoUpdateState.auEnd,
			                            new AutoUpdateStateEnd());
		}

		private void HttpRelease()
		{
			lock(m_Lock)
			{
				if (m_HttpClient != null)
				{
					m_HttpClient.Dispose();
					m_HttpClient = null;
				}
			}
		}

		private void TasksRelease()
		{
			lock(m_Lock)
			{
				if (m_TaskList != null)
				{
					m_TaskList.Clear();
				}
			}
		}

		public void Release()
		{
			HttpRelease();
			TasksRelease();
		}

		internal void ChangeState(AutoUpdateState state)
		{
			Release();
			lock(m_Lock)
			{
				m_StateMgr.ChangeState(state);
			}
		}

		internal void ServerFileListToClientFileList()
		{
			if (!string.IsNullOrEmpty(m_WritePath))
			{
				m_LocalResListFile.Load(m_ServerResListFile);
				m_ServerResListFile.Clear();
				string fileName = string.Format("{0}/{1}", m_WritePath, AutoUpdateMgr._cFileListTxt);
				m_LocalResListFile.SaveToFile(fileName);
			}
		}

		internal void ServerResVerToClientResVer()
		{
			if (IsVersionTxtNoUpdate())
				return;

			if (string.IsNullOrEmpty(m_WritePath))
				return;
			string fileName = string.Format("{0}/{1}", m_WritePath, _cVersionTxt);
			LocalResVersion = CurrServeResrVersion;
			LocalFileListContentMd5 = ServerFileListContentMd5;

			FileStream stream = new FileStream(fileName, FileMode.Create, FileAccess.Write);
			try
			{
				string s = string.Format("res={0}\r\nfileList={1}", LocalResVersion, LocalFileListContentMd5);
				byte[] bytes = System.Text.Encoding.ASCII.GetBytes(s);
				stream.Write(bytes, 0, bytes.Length);
			} finally
			{
				stream.Close();
				stream.Dispose();
				stream = null;
			}
		}

		internal void ChangeUpdateFileNames()
		{
			if (string.IsNullOrEmpty(m_WritePath))
				return;

			var iter = m_UpdateFile.GetIter();
			while (iter.MoveNext())
			{
				string fileName = string.Format("{0}/{1}", m_WritePath, iter.Current.Key);
				if (File.Exists(fileName))
				{
					string fileNameMd5 = m_LocalResListFile.FindFileNameMd5(iter.Current.Key);
					if (!string.IsNullOrEmpty(fileNameMd5))
					{
						string newFileName = string.Format("{0}/{1}", m_WritePath, fileNameMd5);
						if (File.Exists(newFileName))
							File.Delete(newFileName);
						File.Move(fileName, newFileName);
					}
				}
			}
			iter.Dispose();

			m_UpdateFile.Clear();
			string updateFileName = string.Format("{0}/{1}", m_WritePath, _cUpdateTxt);
			if (File.Exists(updateFileName))
				File.Delete(updateFileName);
		}

		// 开始
		public void StartAutoUpdate()
		{
			DownProcess = 0;
			lock(m_Lock)
			{
				m_StateMgr.ChangeState(AutoUpdateState.auPrepare);
			}
		}

		public void EndAutoUpdate()
		{
			Release();
			lock(m_Lock)
			{
				m_StateMgr.ChangeState(AutoUpdateState.auEnd);
			}
		}

		public HttpClient CreateHttpTxt(string url, Action<HttpClientResponse, long> OnReadEvt, 
		                                Action<HttpClientResponse, int> OnErrorEvt)
		{
			HttpRelease();
			HttpClientStrResponse response = new HttpClientStrResponse();
			response.OnReadEvt = OnReadEvt;
			response.OnErrorEvt = OnErrorEvt;
			m_HttpClient = new HttpClient(url, response, 5.0f);
			return m_HttpClient;
		}

		public HttpClient CreateHttpFile(string url, long process, Action<HttpClientResponse, long> OnReadEvt,
		                                 Action<HttpClientResponse, int> OnErrorEvt)
		{
			if (string.IsNullOrEmpty(m_WritePath))
				return null;
			string fileName = Path.GetFileName(url);
			string dstFileName = string.Format("{0}/{1}", m_WritePath, fileName);
			HttpRelease();
			HttpClientFileStream response = new HttpClientFileStream(dstFileName, process, 1024 * 64);
			response.OnReadEvt = OnReadEvt;
			response.OnErrorEvt = OnErrorEvt;
			lock(m_Lock)
			{
				m_HttpClient = new HttpClient(url, response, process, 5.0f);
			}
			return m_HttpClient;
		}

		public WWWFileLoadTask CreateWWWStreamAssets(string fileName, bool usePlatform)
		{
			WWWFileLoadTask ret = WWWFileLoadTask.LoadFileAtStreamingAssetsPath(fileName, usePlatform);
			lock(m_Lock)
			{
				m_TaskList.AddTask(ret, true);
			}
			return ret;
		}

		public string ResServerAddr
		{
			get
			{
				return "http://192.168.199.147:1983";
			}
		}

		internal string CurrServeResrVersion
		{
			get;
			set;
		}

		internal string ServerFileListContentMd5
		{
			get;
			set;
		}

		internal string LocalResVersion
		{
			get;
			set;
		}

		internal string LocalFileListContentMd5
		{
			get;
			set;
		}

		internal void Error(AutoUpdateErrorType errType, int status)
		{}

		public void Update()
		{
			TasksUpdate();
			StateUpdate();
		}

		void TasksUpdate()
		{
			lock(m_Lock)
			{
				if (m_TaskList != null)
					m_TaskList.Process();
			}
		}

		void StateUpdate()
		{
			lock(m_Lock)
			{
				if (m_StateMgr != null)
					m_StateMgr.Process(this);
			}
		}

		public Action<int, long, bool> OnDownloadFileEvt
		{
			get;
			set;
		}

		public float DownProcess
		{
			get
			{
				float ret;
				lock(m_Lock)
				{
					ret = m_DownProcess;
				}
				
				return ret;
			}
			
			internal set
			{
				lock(m_Lock)
				{
					m_DownProcess = value;
				}
			}
		}

		internal void CallDownloadFileEvt(int idx, long readBytes, bool isDone)
		{
			if (OnDownloadFileEvt != null)
				OnDownloadFileEvt(idx, readBytes, isDone);
		}

		private bool GetResVer(string content, out string ver, out string fileListMd5)
		{
			ver = string.Empty;
			fileListMd5 = string.Empty;
			if (string.IsNullOrEmpty(content))
				return false;
			char[] split = new char[1];
			split[0] = '\n';
			string[] lines = content.Split(split, StringSplitOptions.RemoveEmptyEntries);
			if (lines == null || lines.Length <= 0)
				return false;
			string resVer = string.Empty;
			for (int i = 0; i < lines.Length; ++i)
			{
				string line = lines[i].Trim();
				if (string.IsNullOrEmpty(line))
					continue;
				int idx = line.IndexOf('=');
				if (idx >= 0)
				{
					string preStr = line.Substring(0, idx);
					string valueStr = line.Substring(idx + 1);
					if (string.Compare(preStr, "res", StringComparison.CurrentCultureIgnoreCase) == 0)
					{
						ver = valueStr.Trim();
					} else
					if (string.Compare(preStr, "fileList", StringComparison.CurrentCultureIgnoreCase) == 0)
					{
						fileListMd5 = valueStr.Trim();
					}
				}
			}

			return true;
		}

		internal void DownloadUpdateToUpdateTxt(AutoUpdateCfgItem item)
		{
			if (m_UpdateFile == null)
				return;
			if (m_UpdateFile.DownloadUpdate(item))
				m_UpdateFile.SaveToLastFile();
		}

		internal void UpdateToUpdateTxt(ResListFile.ResDiffInfo[] newInfos)
		{
			if (newInfos == null)
				return;
			if (newInfos.Length <= 0)
			{
				m_UpdateFile.RemoveAllDowningFiles();
				m_UpdateFile.Clear();
				m_UpdateFile.SaveToLastFile();
				return;
			}

			if (m_UpdateFile.UpdateToRemoveFiles(newInfos))
				m_UpdateFile.SaveToLastFile();
		}

		internal void LoadServerResVer(string ver)
		{
			if (string.IsNullOrEmpty(ver))
				return;

			string v;
			string fileListMd5;
			if (GetResVer(ver, out v, out fileListMd5))
			{
				CurrServeResrVersion = v;
				ServerFileListContentMd5 = fileListMd5;
			}
		}

		internal ResListFile LocalResListFile
		{
			get
			{
				return m_LocalResListFile;
			}
		}

		internal ResListFile ServerResListFile
		{
			get
			{
				return m_ServerResListFile;
			}
		}

		internal AutoUpdateCfgFile LocalUpdateFile
		{
			get
			{
				return m_UpdateFile;
			}
		}

		internal static readonly string _cVersionTxt = "version.txt";
		internal static readonly string _cFileListTxt = "fileList.txt";
		internal static readonly string _cUpdateTxt = "update.txt";
		
		private AutoUpdateStateMgr m_StateMgr = null;
		private HttpClient m_HttpClient = null;
		private string m_WritePath = string.Empty;
		private TaskList m_TaskList = new TaskList();
		private ResListFile m_LocalResListFile = new ResListFile();
		private ResListFile m_ServerResListFile = new ResListFile();
		private AutoUpdateCfgFile m_UpdateFile = null;
		private float m_DownProcess = 0;
		private object m_Lock = new object();
	}
}