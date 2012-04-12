#region License
// spidernet: db_mgr.cs
// 
// Author:
// 	nsnail (taokeu@gmail.com)
// 
// Copyright (C) 2009 - 2012 beta-1.cn
// 
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

namespace spidernet
{
	internal sealed class db_mgr
	{
		// Fields (4) 

		private int _cache_cnt = cmd_opts.__cache_default;
		/// <summary>
		/// 抓取数据存储文件是否创建的标示.
		/// </summary>
		private bool _db_created;
		private string _db_path;
		private List<Tuple<string, string, string, DateTime>> _inner_cache = new List<Tuple<string, string, string, DateTime>>();

		// Constructors (2) 

		public db_mgr(string db_path, int cache_cnt)
			: this(db_path)
		{
			_cache_cnt = cache_cnt;
		}

		public db_mgr(string db_path)
		{
			_db_path = db_path;
			_db_created = File.Exists(_db_path);
		}

		// Methods (2) 

		// Public Methods (1) 

		public void write_to_db(string url, string title, string html, DateTime create_time)
		{
			lock (_inner_cache)
			{
				_inner_cache.Add(new Tuple<string, string, string, DateTime>(url, title, html, create_time));
				if (_inner_cache.Count >= _cache_cnt)
				{

					//写入数据库
					if (!_db_created)
					{
						create_db();
						_db_created = true;
					}
					using (SQLiteConnection conn = new SQLiteConnection("Data Source=" + _db_path))
					{
						conn.Open();
						using (SQLiteTransaction tran = conn.BeginTransaction())
						{
							foreach (Tuple<string, string, string, DateTime> cache in
								_inner_cache)
							{
								SQLiteCommand cmd = new SQLiteCommand(conn);
								cmd.Transaction = tran;
								cmd.CommandText = "insert into crawl values(@url, @title, @html, @createtime)";
								cmd.Parameters.AddRange(new[] {
									new SQLiteParameter("@url", cache.Item1),
									new SQLiteParameter("@title", cache.Item2),
									new SQLiteParameter("@html", cache.Item3),
									new SQLiteParameter("@createtime", cache.Item4)
								});
								cmd.ExecuteNonQuery();
							}
							tran.Commit();
						}
					}
					_inner_cache.Clear();
				}

			}
		}
		// Private Methods (1) 

		/// <summary>
		/// 创建sqlite数据库, 和表.
		/// </summary>
		private void create_db()
		{
			using (SQLiteConnection conn = new SQLiteConnection("Data Source=" + _db_path))
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
	}
}
