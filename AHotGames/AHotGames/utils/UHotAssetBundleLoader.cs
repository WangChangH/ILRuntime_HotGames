﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public class UHotAssetBundleLoader : AHotBase
{
	private static UHotAssetBundleLoader sinstance;
	public static UHotAssetBundleLoader Instance
	{
		get
		{
			if (sinstance == null)
			{
				var obj = new GameObject("UHotAssetBundleLoader");
				sinstance = new UHotAssetBundleLoader();
				sinstance.SetGameObj(obj, "");
			}
			return sinstance;
		}
	}
	public T OnLoadAsset<T>(string path) where T : UnityEngine.Object
	{
		if (!Environment.UseAB && Environment.IsEditor)
		{
			LoadAnotherClass(Utils.GetAssetBundleName(path), path);
			return null;
		}
		else
		{
			var deps = OnGetAssetBundleDependeces(path + AssetBundleSuffix);
			foreach (var dep in deps)
			{
				OnGetAssetBundle(dep);
			}
			var asset = OnGetAssetBundle(path);
			if (asset == null)
			{
				return null;
			}
			return asset.LoadAsset<T>(Utils.GetAssetBundleName(path));
		}
	}
	private AssetBundle DoGetAssetBundle(string assetBundlePath)
	{
		if (dAssetBundles.ContainsKey(assetBundlePath))
		{
			return dAssetBundles[assetBundlePath];
		}
		var path = Utils.ConfigSaveDir + "/" + assetBundlePath;
		if (!File.Exists(path))
		{
			return null;
		}
		var ab = AssetBundle.LoadFromFile(path);
		if (ab == null)
		{
			return null;
		}
		dAssetBundles.Add(assetBundlePath, ab);
		var depends = OnGetAssetBundleDependeces(assetBundlePath);
		foreach (var d in depends)
		{
			OnGetAssetBundle(assetBundlePath);
		}
		return ab;
	}
	public static string AssetBundleSuffix = ".ab";
	public AssetBundle OnGetAssetBundle(string assetBundlePath, bool NoDependences = false)
	{
		var platform = Utils.GetPlatformFolder(Application.platform);
		if (!assetBundlePath.EndsWith(AssetBundleSuffix))
		{
			assetBundlePath += AssetBundleSuffix;
		}
		assetBundlePath = assetBundlePath.ToLower();
		if (!NoDependences && dAssetBundles.ContainsKey(platform))
		{
			if (manifestBundle == null)
			{
				manifestBundle = dAssetBundles[platform];
			}
			if (manifest == null)
			{
				manifest = (AssetBundleManifest)manifestBundle.LoadAsset("AssetBundleManifest");
			}
		}
		if (dAssetBundles.ContainsKey(assetBundlePath))
		{
			return dAssetBundles[assetBundlePath];
		}
		return DoGetAssetBundle(assetBundlePath);
	}
	AssetBundle manifestBundle;
	AssetBundleManifest manifest;
	public string[] OnGetAssetBundleDependeces(string name, List<string> dependens = null)
	{
		name = name.StartsWith("/") ? name.Substring(1) : name;
		var platform = Utils.GetPlatformFolder(Application.platform);
		if (!dAssetBundles.ContainsKey(platform))
		{
			var ab = DoGetAssetBundle(platform);
			if (ab == null)
			{
				return new string[] { };
			}
		}
		if (manifestBundle == null)
		{
			manifestBundle = dAssetBundles[platform];
		}
		if (manifest == null)
		{
			manifest = (AssetBundleManifest)manifestBundle.LoadAsset("AssetBundleManifest");
		}
		var total = dependens;
		if (dependens != null)
		{
			foreach (var d in dependens)
			{
				if (!total.Contains(d))
				{
					total.Add(d);
				}
			}
		}
		else
		{
			total = new List<string>();
		}
		var result = manifest.GetAllDependencies(name);
		foreach (var d in result)
		{
			if (!total.Contains(d))
			{
				total.Add(d);
			}
		}
		foreach (var r in result)
		{
			if (dependens != null && dependens.Contains(r))
			{
				continue;
			}
			var deps = OnGetAssetBundleDependeces(r, total);
			foreach (var d in deps)
			{
				if (!total.Contains(d))
				{
					total.Add(d);
				}
			}
		}
		return total.ToArray();
	}
	private Dictionary<string, AssetBundle> dAssetBundles = new Dictionary<string, AssetBundle>();
	private List<string> lDownloaded = new List<string>();
	Dictionary<string, string> dRemoteVersions = new Dictionary<string, string>();
	public void OnDownloadResources(List<string> lResources, Action downloaded, Action<float> progress = null)
	{
		if (!Environment.UseAB)
		{
			downloaded?.Invoke();
			return;
		}
		if (dRemoteVersions.Count == 0)
		{
			OnDownloadText(Utils.GetPlatformFolder(Application.platform) + "/versions", (content) =>
			{
				var acontent = content.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
				foreach (var c in acontent)
				{
					var ac = c.Split('|');
					if (ac.Length < 2)
					{
						continue;
					}
					if (!dRemoteVersions.ContainsKey(ac[0]))
					{
						dRemoteVersions.Add(ac[0], ac[1]);
					}
				}
				DoCheckVersions(lResources, downloaded, progress);
			});
		}
		else
		{
			DoCheckVersions(lResources, downloaded, progress);
		}
	}
	private void DoCheckVersions(List<string> lResources, Action downloaded, Action<float> progress)
	{
		var lNeedDownload = new List<string>();
		foreach (var r in lResources)
		{
			var res = r;
			if (!res.StartsWith("/"))
			{
				res = $"/{r}";
			}
			res = res.ToLower();
			if (!dRemoteVersions.ContainsKey(res))
			{
				continue;
			}
			var file = ULocalFileManager.Instance.OnGetFile(res);
			if (file == null || file.version != dRemoteVersions[res])
			{
				UDebugHotLog.Log($"{res} need download");
				lNeedDownload.Add(res);
			}
			if (res.EndsWith(AssetBundleSuffix))
			{
				var deps = OnGetAssetBundleDependeces(res);
				foreach (var dep in deps)
				{
					if (!lNeedDownload.Contains(dep))
					{
						var rdep = dep;
						if (!dep.StartsWith("/"))
						{
							rdep = $"/{dep}";
						}
						if (!dRemoteVersions.ContainsKey(rdep))
						{
							continue;
						}
						file = ULocalFileManager.Instance.OnGetFile(rdep);
						if (file == null || file.version != dRemoteVersions[rdep])
						{
							lNeedDownload.Add(rdep);
						}
					}
				}
			}
		}
		totalCount = lNeedDownload.Count;
		UDebugHotLog.Log($"totalCount {totalCount}");
		DoDownloadResources(lNeedDownload, downloaded, progress);
	}
	int totalCount;
	private void DoDownloadResources(List<string> lWaitingForDownload, Action downloaded, Action<float> progress)
	{
		if (lWaitingForDownload.Count > 0)
		{
			var resource = "";
			do
			{
				if (lWaitingForDownload.Count == 0)
				{
					resource = "";
					break;
				}
				resource = lWaitingForDownload[0];
				lWaitingForDownload.RemoveAt(0);
			}
			while (dAssetBundles.ContainsKey(resource));

			if (!string.IsNullOrEmpty(resource))
			{
				WWW w = OnDownloadBytes(resource
					, dRemoteVersions[resource]
					, (res) =>
					{
						lDownloaded.Add(res);
						DoDownloadResources(lWaitingForDownload, downloaded, progress);
					}
					, (err) =>
					{
						//lResources.Add(resource);
						DoDownloadResources(lWaitingForDownload, downloaded, progress);
					}
					, (p) =>
					{
						fProgress = (float)lDownloaded.Count / totalCount + p / totalCount;
						if (progress != null)
						{
							progress(fProgress);
						}
						else
						{
							UILoading.Instance?.OnSetProgress(fProgress);
						}
					});
				return;
			}
		}
		totalCount = 0;
		lDownloaded.Clear();
		fProgress = -1;
		progress?.Invoke(1);
		downloaded?.Invoke();
	}
	public float fProgress = -1;
	private WWW OnDownloadBytes(string resource
		 , string version
		 , Action<string> downloadedAction
		 , Action<string> errorAction = null
		 , Action<float> progressAction = null
	 )
	{
		if (!Environment.UseAB)
		{
			return null;
		}
		var url = Utils.BaseURL + Utils.GetPlatformFolder(Application.platform) + resource;
		var www = new WWW(url);
		addUpdateAction(() =>
		{
			if (www.isDone)
			{
				progressAction?.Invoke(1);
				if (string.IsNullOrEmpty(www.error))
				{
					var filepath = Utils.ConfigSaveDir + resource;
					var fi = new FileInfo(filepath);
					if (!fi.Directory.Exists)
					{
						fi.Directory.Create();
					}
					File.WriteAllBytes(filepath, www.bytes);
					ULocalFileManager.Instance.OnAddFile(resource, version);
					downloadedAction?.Invoke(resource);
				}
				else
				{
					UDebugHotLog.Log($"{url} error {www.error}");
					errorAction?.Invoke(www.error);
				}
				return true;
			}
			else
			{
				if (progressAction != null)
					progressAction(www.progress);
				else
				{
					UDebugHotLog.Log($"OnDownloadBytes process {www.progress}");
					UILoading.Instance?.OnSetProgress(www.progress);
				}
			}
			return false;
		});
		return www;
	}
	private WWW OnDownloadText(string resource, Action<string> downloadedAction, Action<string> errorAction = null)
	{
		if (!Environment.UseAB)
		{
			return null;
		}
		var url = Utils.BaseURL + resource + ".txt";
		var www = new WWW(url);
		addUpdateAction(() =>
		{
			if (www.isDone)
			{
				if (string.IsNullOrEmpty(www.error))
				{
					lDownloaded.Add(resource);
					downloadedAction?.Invoke(www.text);
				}
				else
				{
					UDebugHotLog.Log($"{www.url} error {www.error}");
					errorAction?.Invoke(www.error);
				}
				return true;
			}
			return false;
		});
		return www;
	}

	protected override void InitComponents() { }
}
