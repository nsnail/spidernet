#region License
// spidernet: cmd_opt.cs
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
using CommandLine;
using CommandLine.Text;

namespace spidernet
{
	internal sealed class cmd_opts : CommandLineOptionsBase
	{
		public const int __cache_default = 500;

		[Option(null, "dbpath", HelpText = "设置抓取数据存储文件路径. [default: {CURRPATH}\\spidernet.db3]")]
		public string _db_path = "spidernet.db3";
		[Option("c", "dbcache", HelpText = "数据文件写入缓存(n).  [default: 500]")]
		public int _db_cache = __cache_default;
		[Option("t", "thread", HelpText = "设置爬虫线程数量. [default: 10]")]
		public int _thread_cnt = 10;
		[Option("d", "depth", HelpText = "设置爬行深度(从1开始). [default: 5]")]
		public int _crawl_depth = 5;
		[Option("u", "starturl", Required = true, HelpText = "设置爬行起始url地址.")]
		public string _url_start;
		[Option("m", "maxbytes", HelpText = "获取资源时最大下载字节数(byte).  [default: 1MB]")]
		public int _bytes_max = 1024 * 1024;
		[Option(null, "timeout", HelpText = "获取资源超时时间(ms).  [default: 20s]")]
		public int _timeout = 20 * 1000;



		[HelpOption(
			HelpText = "显示帮助.")]
		public string GetUsage()
		{
			HelpText help = create_helptext(prog._heading_info);
			parsing_err_help(help);
			help.AddPreOptionsLine("This is free software. You may redistribute copies of it under the terms of");
			help.AddPreOptionsLine("the MIT License <http://www.opensource.org/licenses/mit-license.php>.");
			help.AddPreOptionsLine("Usage: spidernet -uhttp://www.sina.com/");
			help.AddPreOptionsLine(string.Format("       spidernet -uhttp://www.sina.com/ -t10 -d5 -m100000 --timeout 20000 --dbpath c:\\spidernet.db3"));
			help.AddOptions(this);

			return help;
		}

		public HelpText create_helptext(string heading_info)
		{
			var help = new HelpText(heading_info)
			{
				AdditionalNewLineAfterOption = true,
				Copyright = new CopyrightInfo("beta-1.cn/nsnail(taokeu@gmail.com)", 2012)
			};
			return help;
		}

		private void parsing_err_help(HelpText help)
		{
			string errors = help.RenderParsingErrorsText(this, 2);
			if (!string.IsNullOrEmpty(errors))
				help.AddPreOptionsLine(
					string.Concat(Environment.NewLine, "ERROR: ", errors, Environment.NewLine)
					);
		}
	}
}
