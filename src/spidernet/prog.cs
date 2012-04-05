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
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using CommandLine;
using CommandLine.Text;
using System.Diagnostics;

namespace spidernet
{
	internal sealed class prog
	{
		/// <summary>
		/// 抓取数据存储文件是否创建的标示.
		/// </summary>
		private static bool _db_created;
		/// <summary>
		/// 版本描述
		/// </summary>
		public static readonly HeadingInfo _heading_info = new HeadingInfo("sipdernet", "v0.1");
		/// <summary>
		/// 命令行参数
		/// </summary>
		private static readonly cmd_opts _opts = new cmd_opts();
		private static readonly Regex _regex_charset = new Regex("\\<meta.*?charset=(.*?)\\\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		private static readonly Regex _regex_href = new Regex("href=\"(http://.*?)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		//TODO: 目前暂未匹配相对链接
		private static readonly Regex _regex_title = new Regex("\\<title\\>((?:.|\n)*?)\\</title\\>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
		/// <summary>
		/// url树
		/// </summary>
		private static node _root_node;
		private static Dictionary<Thread, string> _thread_msg_dic;
		/// <summary>
		/// 程序统计信息
		/// </summary>
		private static prog_cter prog_stat;
		/// <summary>
		/// 获取资源, 构建和遍历和url树.
		/// </summary>
		/// <param name="start_node">起始节点</param>
		private static void crawl(ref node start_node)
		{
			if (start_node.depth > _opts.crawl_depth) return; //爬行深度超过限制, 退出.

			//锁定node
			lock (start_node)
			{
				if (start_node.locked)
					return;
				start_node.locked = true;
			}

			//资源解码后存储的str
			string html = null;
			try
			{
				HttpWebRequest req = WebRequest.Create(start_node.url) as HttpWebRequest;
				req.Proxy = null;
				req.Timeout = _opts.timeout;
				req.AllowAutoRedirect = false; //TODO: 避免死递归, 禁止301等重定向.(需改进)
				byte[] down_bytes = new byte[_opts.bytes_max];
				byte[] down_buf = new byte[down_bytes.Length / 10]; //缓存区, 1/10的容器大小.
				int down_bytes_cursor = 0;//容器填充位置
				using (WebResponse rep = req.GetResponse())
				{
					++prog_stat.res_downloaded;
					if (rep.Headers[HttpResponseHeader.ContentType] == null
						|| !rep.Headers[HttpResponseHeader.ContentType].Contains("text/html"))
					{
						start_node.valid = false;//过滤非text/html资源类型
						return;
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
							int decode_bytes_cursor = 0;//容器填充位置
							int read_size;
							while ((read_size = gs.Read(decode_buf, 0, decode_buf.Length)) > 0)
							{
								//超过最大下载字节数限制,跳出.
								if (decode_bytes_cursor + read_size > decode_bytes.Length) break;
								Array.Copy(down_buf, 0, decode_bytes, decode_bytes_cursor, read_size);
								decode_bytes_cursor += read_size;
							}
							down_bytes = decode_bytes;
						}
					}


					//TODO: 目前仅支持utf-8和gbk编码的资源.
					if (rep.Headers[HttpResponseHeader.ContentType] != null)
					{
						if (rep.Headers[HttpResponseHeader.ContentType].Contains("utf-8"))
							html = Encoding.UTF8.GetString(down_bytes);
						else if (rep.Headers[HttpResponseHeader.ContentType].Contains("gb2312")
							|| rep.Headers[HttpResponseHeader.ContentType].Contains("gbk"))
							html = Encoding.GetEncoding(54936).GetString(down_bytes);
					}
				}

				if (html == null)
				{
					//http head中未指定encoding的资源, 暂以utf8统一解码.
					html = Encoding.UTF8.GetString(down_bytes);

					Match m = _regex_charset.Match(html);//寻找<meta>标记中的charset
					if (!m.Success)
					{
						//http body中也没有指定encoding, 标记资源无效.
						start_node.valid = false;
						return;
					}

					//http body中指定了gbk编码
					if (m.Groups[1].Value.IndexOf("gb2312", StringComparison.OrdinalIgnoreCase) >= 0
						|| m.Groups[1].Value.IndexOf("gbk", StringComparison.OrdinalIgnoreCase) >= 0)
						html = Encoding.GetEncoding(54936).GetString(down_bytes);

				}
			}
			catch (Exception)
			{
				//TODO: 记录异常日志.
			}

			if (html == null)
			{
				//str容器未得到设置, 标记node无效.
				start_node.valid = false;
				return;
			}

			{
				//匹配html中的href,将这些url作为自己的children集合.
				MatchCollection mc = _regex_href.Matches(html);
				IEnumerable<string> mc_str =
					(mc.AsParallel() as IEnumerable<Object>).Select(f => (f as Match).Groups[1].Value).Distinct();
				foreach (string u in mc_str)
				{
					node child = new node { url = u, parent = start_node, depth = start_node.depth + 1 };//深度+1
					if (isdup(u, _root_node)) continue;//url重复检查.
					lock (start_node)
						start_node.children.Add(child);//添加children,这些新的children是未lock状态, 会马上被空闲的线程抢到并遍历.
				}
			}

			//遍历child
			for (int i = 0; i != start_node.children.Count; ++i)
			{
				node child = start_node.children[i];
				lock (_thread_msg_dic)
					_thread_msg_dic[Thread.CurrentThread] = child.url;
				crawl(ref child);
			}

			if (!start_node.valid) return; //无效节点, 不写入数据库.

			//写入数据库
			if (!_db_created)
			{
				create_db(_opts.db_path);
				_db_created = true;
			}
			using (SQLiteConnection conn = new SQLiteConnection("Data Source=" + _opts.db_path))
			using (SQLiteCommand cmd = new SQLiteCommand(conn))
			{

				conn.Open();
				cmd.CommandText = "insert into crawl values(@url,@title,@html,@createtime)";

				string title = null;
				Match m = _regex_title.Match(html);//获取html中<title>内容
				if (m.Success)
					title = m.Groups[1].Value.Replace("\n", "").Replace("\r", "").Trim();

				cmd.Parameters.AddRange(new[] {
			        new SQLiteParameter("@url", start_node.url),
			        new SQLiteParameter("@title", title),
			        new SQLiteParameter("@html", html),
                    new SQLiteParameter("@createtime", DateTime.Now)
				                              });
				cmd.ExecuteNonQuery();
				++prog_stat.res_stored;
			}
		}
		/// <summary>
		/// 创建sqlite数据库, 和表.
		/// </summary>
		/// <param name="db_path">数据库文件路径</param>
		private static void create_db(string db_path)
		{
			using (SQLiteConnection conn = new SQLiteConnection("Data Source=" + db_path))
			using (SQLiteCommand cmd = new SQLiteCommand(conn))
			{
				conn.Open();
				cmd.CommandText = @"CREATE TABLE [crawl] (
  [url] NVARCHAR, 
  [title] NVARCHAR, 
  [html] NTEXT, 
  [createtime] DATETIME, 
  CONSTRAINT [sqlite_autoindex_crawl_1] PRIMARY KEY ([url]) ON CONFLICT IGNORE);";
				cmd.ExecuteNonQuery();
			}
		}
		/// <summary>
		/// 获取未锁定的url节点.
		/// </summary>
		/// <param name="root">树根</param>
		/// <returns></returns>
		private static node find_nolock_node(node root)
		{
			if (!root.locked) return root;
			return root.children.Count == 0 ? null
				: root.children.Select(find_nolock_node).FirstOrDefault(f => f != null);
		}
		/// <summary>
		/// 检查指定url是否在整棵树中存在, 避免死循环.
		/// </summary>
		/// <param name="url">检查url</param>
		/// <param name="root">树根</param>
		/// <returns>存在重复返回true</returns>
		private static bool isdup(string url, node root)
		{
			lock (root)
			{
				foreach (node child in root.children)
				{
					if (child.url == url) return true;
					if (isdup(url, child)) return true;
				}
				return false;
			}
		}
		private static void Main(string[] args)
		{
			//解析命令行参数
			ICommandLineParser parser
				= new CommandLineParser(new CommandLineParserSettings(Console.Error));
			if (!parser.ParseArguments(args, _opts))
				Environment.Exit(1);

			_root_node = new node { url = _opts.url_start };
			_db_created = File.Exists(_opts.db_path);

			//启动工作线程
			_thread_msg_dic = new Dictionary<Thread, string>(_opts.thread_cnt);
			for (int i = 0; i != _opts.thread_cnt; ++i)
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
				Console.WriteLine("mem used: {0} KB", (proc_self.WorkingSet64 / 1024).ToString("###,###"));
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
