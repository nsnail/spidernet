using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace spidernet
{
	/// <summary>
	/// 资源过滤器
	/// </summary>
	internal sealed class filter
	{
		/// <summary>
		/// 过滤不恰当的href
		/// </summary>
		/// <returns>返回过滤器处理之后的href,若不通过,返回null</returns>
		public static string filter_href(string origin_href, string prefix)
		{
			if (origin_href == "#") return null;
			if (origin_href.IndexOf("javascript:", StringComparison.OrdinalIgnoreCase) == 0)
				return null;

			//相对链接, 在其前附加父url
			if (origin_href.IndexOf("http://", StringComparison.OrdinalIgnoreCase) != 0
				&& origin_href.IndexOf("https://", StringComparison.OrdinalIgnoreCase) != 0
				)
			{
				int last_slash = prefix.LastIndexOf('/');
				if (last_slash == 6) //最后一个'/'是http://
					origin_href = prefix + "/" + origin_href.TrimStart('/');
				else
					origin_href = prefix.Substring(0, last_slash) + "/" + origin_href.TrimStart('/');
			}

			return origin_href;
		}
	}
}
