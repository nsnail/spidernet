#region License
// spidernet: node.cs
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

namespace spidernet
{
	/// <summary>
	/// 表示url树中的节点.
	/// </summary>
	[Serializable]
	internal sealed class node
	{
		/// <summary>
		/// url地址
		/// </summary>
		public string url;

		/// <summary>
		/// 子节点集合
		/// </summary>
		public List<node> children = new List<node>();

		/// <summary>
		/// 父节点
		/// </summary>
		public node parent;

		/// <summary>
		/// 资源是否有效, 例如.png属于无效资源, 将不会被存储至数据库
		/// </summary>
		public bool valid = true;

		/// <summary>
		/// 已锁定, 该节点正在被某线程遍历.
		/// </summary>
		public bool locked;

		/// <summary>
		/// 当前节点深度.
		/// </summary>
		public int depth = 1;
	}
}
