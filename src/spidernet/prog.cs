#region License
// spidernet: prog.cs
// 
// Author:
// 	nsnail (taokeu@gmail.com)
// 
// Copyright (C) 2009 - 2012 beta-1.cn
// 
// The MIT License (MIT)
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
#endregion
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using CommandLine;
using CommandLine.Text;
using log4net;

namespace spidernet
{
	internal sealed class prog
	{
		/// <summary>
		/// 日志记录器
		/// </summary>
		private static readonly ILog _log = LogManager.GetLogger(typeof(prog));

		private static db_mgr _dbm;

		/// <summary>
		/// 版本描述
		/// </summary>
		public static readonly HeadingInfo _heading_info = new HeadingInfo("sipdernet", "v0.1");
		/// <summary>
		/// 命令行参数
		/// </summary>
		private static readonly cmd_opts _opts = new cmd_opts();
		private static readonly Regex _regex_charset = new Regex("\\<meta.*?charset=(.*?)\\\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex _regex_href = new Regex("\\<a.+?href=\"(.*?)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex _regex_title = new Regex("\\<title\\>((?:.|\n)*?)\\</title\\>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		/// <summary>
		/// url树
		/// </summary>
		private static node _root_node;

		/// <summary>
		/// 线程消息
		/// </summary>
		private static Dictionary<Thread, string> _thread_msg_dic;

		/// <summary>
		/// 已访问的url
		/// </summary>
		private static readonly Dictionary<int, object> _visited_url = new Dictionary<int, object>();

		/// <summary>
		/// 程序统计信息
		/// </summary>
		private static prog_cter prog_stat;

		/// <summary>
		/// 获取资源的纯文本格式.
		/// </summary>
		/// <param name="req_url"></param>
		/// <returns></returns>
		private static string get_res_text(string req_url)
		{
			//资源解码后存储的str
			string res = null;
			try
			{
				HttpWebRequest req = WebRequest.Create(req_url) as HttpWebRequest;
				req.Timeout = _opts._timeout;
				req.AllowAutoRedirect = false; //TODO: 避免死递归, 禁止301等重定向.(需改进)
				byte[] down_bytes = new byte[_opts._bytes_max];
				byte[] down_buf = new byte[down_bytes.Length / 10]; //缓存区, 1/10的容器大小.
				int down_bytes_cursor = 0; //容器填充位置
				using (WebResponse rep = req.GetResponse())
				{
					++prog_stat.res_downloaded;
					if (rep.Headers[HttpResponseHeader.ContentType] == null
						|| !rep.Headers[HttpResponseHeader.ContentType].Contains("text/html"))
					{
						//过滤非text/html资源类型
						return null;
					}
					using (Stream sr = rep.GetResponseStream())
					{
						int read_size;
						while ((read_size = sr.Read(down_buf, 0, down_buf.Length)) > 0)
						{
							//超过最大下载字节数限制,跳出.
							if (down_bytes_cursor + read_size > down_bytes.Length) break;
							Array.Copy(down_buf, 0, down_bytes, down_bytes_cursor, read_size);
							down_bytes_cursor += read_size;
						}
					}

					//TODO: 目前仅支持gzip解压缩
					if (rep.Headers[HttpResponseHeader.ContentEncoding] == "gzip")
					{
						using (MemoryStream ms = new MemoryStream(down_bytes))
						using (GZipStream gs = new GZipStream(ms, CompressionMode.Decompress))
						{
							byte[] decode_bytes = new byte[down_bytes.Length];
							byte[] decode_buf = new byte[down_bytes.Length / 10];
							int decode_bytes_cursor = 0; //容器填充位置
							int read_size;
							while ((read_size = gs.Read(decode_buf, 0, decode_buf.Length)) > 0)
							{
								//超过最大下载字节数限制,跳出.
								if (decode_bytes_cursor + read_size > decode_bytes.Length) break;
								Array.Copy(down_buf, 0, decode_bytes, decode_bytes_cursor, read_size);
								decode_bytes_cursor += read_size;
							}
							down_bytes = decode_bytes;
							down_bytes_cursor = decode_bytes_cursor;
						}
					}


					//TODO: 目前仅支持utf-8和gbk编码的资源.
					if (rep.Headers[HttpResponseHeader.ContentType] != null)
					{
						if (rep.Headers[HttpResponseHeader.ContentType].Contains("utf-8"))
							res = Encoding.UTF8.GetString(down_bytes, 0, down_bytes_cursor);
						else if (rep.Headers[HttpResponseHeader.ContentType].Contains("gb2312")
								 || rep.Headers[HttpResponseHeader.ContentType].Contains("gbk"))
							res = Encoding.GetEncoding(54936).GetString(down_bytes, 0, down_bytes_cursor);
					}
				}

				if (res == null)
				{
					//http head中未指定encoding的资源, 暂以utf8统一解码.
					res = Encoding.UTF8.GetString(down_bytes, 0, down_bytes_cursor);

					Match m = _regex_charset.Match(res); //寻找<meta>标记中的charset
					if (!m.Success)
					{
						//http body中也没有指定encoding, 资源无效.
						return null;
					}

					//http body中指定了gbk编码
					if (m.Groups[1].Value.IndexOf("gb2312", StringComparison.OrdinalIgnoreCase) >= 0
						|| m.Groups[1].Value.IndexOf("gbk", StringComparison.OrdinalIgnoreCase) >= 0)
						res = Encoding.GetEncoding(54936).GetString(down_bytes, 0, down_bytes_cursor);

				}
			}
			catch (Exception)
			{
				//TODO: 记录异常日志.
			}
			return res;
		}

		/// <summary>
		/// 获取资源, 构建和遍历和url树.
		/// </summary>
		/// <param name="start_node">起始节点</param>
		private static void crawl(ref node start_node)
		{
			//锁定node
			lock (start_node)
			{
				if (start_node.locked)
					return;
				start_node.locked = true;
			}
			lock (_log)
				_log.InfoFormat("requesting\t{0}", start_node.url);

			{

				string res = get_res_text(start_node.url);

				if (res == null)
				{
					//str容器未得到设置, 标记node无效.
					start_node.valid = false;
					return;
				}

				string title = null;
				Match m = _regex_title.Match(res); //获取html中<title>内容
				if (m.Success)
					title = m.Groups[1].Value.Replace("\n", "").Replace("\r", "").Trim();
				//写入数据库
				_dbm.write_to_db(start_node.url, title, res, DateTime.Now);
				++prog_stat.res_stored;

				//当前爬行深度已达到限制, 不再增加child.
				if (start_node.depth >= _opts._crawl_depth) return;

				//匹配html中的href,将这些url作为自己的children集合.
				MatchCollection mc = _regex_href.Matches(res);
				IEnumerable<string> mc_str =
					(mc.AsParallel() as IEnumerable<Object>).Select(f => (f as Match).Groups[1].Value).Distinct();
				foreach (string href in mc_str)
				{
					string child_url = filter.filter_href(href, start_node.url);
					if (child_url == null) continue;
					node child = new node { url = child_url, parent = start_node, depth = start_node.depth + 1 }; //深度+1
					if (isdup(child_url)) continue; //url重复检查.
					_visited_url.Add(child_url.GetHashCode(), null);
					lock (start_node)
						start_node.children.Add(child); //添加children,这些新的children是未lock状态, 会马上被空闲的线程抢到并遍历.
				}
			}//释放res

			//遍历child
			for (int i = 0; i != start_node.children.Count; ++i)
			{
				node child = start_node.children[i];
				lock (_thread_msg_dic)
					_thread_msg_dic[Thread.CurrentThread] = "d" + child.depth + "::" + child.url;
				crawl(ref child);
			}
		}

		/// <summary>
		/// 获取未锁定的url节点.
		/// </summary>
		/// <param name="root">树根</param>
		/// <returns></returns>
		private static node find_nolock_node(node root)
		{
			lock (root)
			{
				if (!root.locked) return root;
				return root.children.Count == 0
						? null
						: root.children.Select(find_nolock_node).FirstOrDefault(f => f != null);
			}
		}

		/// <summary>
		/// url重复check
		/// </summary>
		/// <param name="url"></param>
		/// <returns></returns>
		private static bool isdup(string url)
		{
			return _visited_url.ContainsKey(url.GetHashCode());
		}

		private static void Main(string[] args)
		{
			//解析命令行参数
			ICommandLineParser parser
				= new CommandLineParser(new CommandLineParserSettings(Console.Error));
			if (!parser.ParseArguments(args, _opts))
				Environment.Exit(1);

			_root_node = new node { url = _opts._url_start };
			_dbm = new db_mgr(_opts._db_path, _opts._db_cache);

			//启动工作线程
			_thread_msg_dic = new Dictionary<Thread, string>(_opts._thread_cnt);
			for (int i = 0; i != _opts._thread_cnt; ++i)
			{
				Thread t = new Thread(t_work) { IsBackground = true, Name = "t" + i };
				lock (_thread_msg_dic)
					_thread_msg_dic.Add(t, "started");
				t.Start();
			}

			Console.Title = _heading_info;
			while (true)
			{
				Console.Clear();
				Console.WriteLine(_opts.create_helptext(_heading_info));
				Console.WriteLine();
				Process proc_self = Process.GetCurrentProcess();
				Console.WriteLine("working time: {0}", (DateTime.Now - proc_self.StartTime));
				Console.WriteLine("mem usage: {0} KB", (proc_self.WorkingSet64 / 1024).ToString("###,###"));
				Console.WriteLine("res downloaded:{0}\tres stored:{1}", prog_stat.res_downloaded, prog_stat.res_stored);
				Console.WriteLine("threads:");
				lock (_thread_msg_dic)
				{
					foreach (KeyValuePair<Thread, string> kv in _thread_msg_dic)
						Console.WriteLine(kv.Key.Name + "::" + kv.Value);
				}
				Thread.Sleep(1000);
			}
		}
		/// <summary>
		/// 线程工作函数
		/// </summary>
		private static void t_work()
		{
			while (true)//线程不自动退出.
			{
				node node_start;
				//抢占任务模式,
				while ((node_start = find_nolock_node(_root_node)) == null)
				{
					lock (_thread_msg_dic)
						_thread_msg_dic[Thread.CurrentThread] = "wait";
					Thread.Sleep(1000);//未抢到任务,休息1s后继续
				}
				crawl(ref node_start);//爬行开始
			}
		}
	}
}
