﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using LinqToDB.Extensions;
using LinqToDB.Linq;
using LinqToDB.Mapping;
using LinqToDB.SqlQuery;
using LINQPad;
using LINQPad.Extensibility.DataContext;

namespace LinqToDB.LINQPad
{
	static class XmlFormatter
	{
		class Total
		{
			public object           Value;
			public Func<int,object> GetAverage;
			public bool             IsNumber;

			public void Add<T>(Func<T,T> add, Func<T,Func<int,object>> avr)
			{
				if (Value == null)
				{
					Value    = add(default(T));
					IsNumber = true;
				}
				else
				{
					Value = add((T)Value);
				}

				GetAverage = avr((T)Value);
			}
		}

		static int _id;

		public static object Format(MappingSchema mappingSchema, object objectToWrite)
		{
			if (objectToWrite == null || objectToWrite is string || objectToWrite is XElement)
				return objectToWrite;

			if (IsNull(objectToWrite))
				return null;

			if (objectToWrite is SqlDecimal value)
			{
				return Util.RawHtml($"<div class=\"n\">{value}</div>");
			}

			var type = objectToWrite.GetType();

			if (objectToWrite is IEnumerable enumerable)
			{
				var itemType = type.GetItemType();
				var items    = enumerable.Cast<object>().ToList();
				var tableID  = ++_id;

				var columns = mappingSchema.IsScalarType(itemType) ?
					new[]
					{
						new
						{
							MemberType = itemType,
							MemberName = "",
							GetValue = (Func<MappingSchema,object,object>)((ms, v) => v),
							Total    = new Total(),
						}
					}
					:
					mappingSchema.GetEntityDescriptor(itemType).Columns
						.Select(c => new
						{
							c.MemberType,
							c.MemberName,
							GetValue = (Func<MappingSchema,object,object>)c.GetValue,
							Total    = new Total(),
						})
						.ToArray();

				return Util.RawHtml(
					new XElement("div",
						new XAttribute("class", "spacer"),
						new XElement("table",
							new object[]
							{
								new XAttribute("id", $"t{tableID}"),
								new XElement("tr",
									new XElement("td",
										new XAttribute("class",   "typeheader"),
										new XAttribute("colspan", columns.Length),
										new XElement("a",
											new XAttribute("href",  ""),
											new XAttribute("class", "typeheader"),
											new XAttribute("onclick", $"return toggle('t{tableID}');"),
											new XElement("span",
												new XAttribute("class", "typeglyph"),
												new XAttribute("id",    $"t{tableID}ud"),
												5),
											$"{GetTypeName(itemType)} ({items.Count} items)"),
										new XElement("a",
											new XAttribute("href",  ""),
											new XAttribute("class", "extenser"),
											new XAttribute("onclick", "return window.external.CustomClick('0',false);"),
											new XElement("span",
												new XAttribute("class", "extenser"),
												4)))),
								new XElement("tr",
									columns.Select(c =>
										new XElement("th",
											new XAttribute("title", GetTypeName(c.MemberType)),
											$"{c.MemberName}")))
							}
							.Union(items
								.Select(i => new XElement("tr", columns.Select(c => FormatValue(c.Total, c.GetValue(mappingSchema, i)))))
								.ToList())
							.Union(
								new object[]
								{
									new XElement("tr", columns.Select(c =>
										new XElement("td",
											new XAttribute("title", c.Total.Value == null ? "Totals" : $"Total={c.Total.Value}\r\nAverage={c.Total.GetAverage(items.Count)}"),
											new XAttribute("class", "columntotal"),
											new XAttribute("style", "font-size:100%;"),
											c.Total.Value))),
								}
								.Where(_ => columns.Any(c => c.Total.Value != null))
								))));
			}

//			if (!_mappingSchema.IsScalarType(objectToWrite.GetType()))
//			{
//				objectToWrite = Util.RawHtml(new XElement("div", objectToWrite.GetType()));
//				return;
//			}

			//MessageBox.Show($"{objectToWrite.GetType()}\r\n{objectToWrite}");

			return objectToWrite;
		}

		static bool IsAnonymousType(Type type)
		{
			return
				Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute), false) &&
				type.IsGenericType && type.Name.Contains("AnonymousType")            &&
				(type.Name.StartsWith("<>") || type.Name.StartsWith("VB$"))          &&
				(type.Attributes & TypeAttributes.NotPublic) == TypeAttributes.NotPublic;
		}

		static string GetTypeName(Type type)
		{
			if (type.IsNullable())
				return type.ToNullableUnderlying().Name + "?";

			if (IsAnonymousType(type))
				return "new";
			
			return type.Name;
		}

		//value is Oracle.DataAccess.Types.       INullable && ((Oracle.DataAccess.Types.       INullable)value).IsNull ||
		//value is Oracle.ManagedDataAccess.Types.INullable && ((Oracle.ManagedDataAccess.Types.INullable)value).IsNull ||
		//value is IBM.Data.DB2Types.             INullable && ((IBM.Data.DB2Types.             INullable)value).IsNull ||

		//value is NpgsqlTypes.NpgsqlDateTime       && ((NpgsqlTypes.NpgsqlDateTime)value).Kind == DateTimeKind.Unspecified ||

		//value is Sybase.Data.AseClient.AseDecimal && ((Sybase.Data.AseClient.AseDecimal)value).IsNull ||

		//value is MySql.Data.Types.MySqlDecimal    && ((MySql.Data.Types.MySqlDecimal) value).IsNull ||
		//value is MySql.Data.Types.MySqlDateTime   && ((MySql.Data.Types.MySqlDateTime)value).IsNull ||
		//value is MySql.Data.Types.MySqlGeometry   && ((MySql.Data.Types.MySqlGeometry)value).IsNull

		static bool DynamicCheckForNull(string baseType, Type type, object value, ref bool isNull)
		{
			var baseTypeType = Type.GetType(baseType, false);

			if (baseTypeType == null || !baseTypeType.IsSameOrParentOf(type))
				return false;

			var prop = baseTypeType.GetProperty("IsNull");
			if (prop == null)
				return false;

			isNull = (bool) prop.GetValue(value);
			return true;
		}

		static bool DynamicCheckForNull(string baseType, Type type, object value, ref bool isNull, Func<Type, object, bool> checker)
		{
			var baseTypeType = Type.GetType(baseType, false);

			if (baseTypeType == null || !baseTypeType.IsSameOrParentOf(type))
				return false;

			isNull = checker(baseTypeType, value);
			return true;
		}


		static bool DynamicCheckNpgsqlDateTime(Type type, object value, ref bool isNull)
		{
			//value is NpgsqlTypes.NpgsqlDateTime       && ((NpgsqlTypes.NpgsqlDateTime)value).Kind == DateTimeKind.Unspecified ||

			return DynamicCheckForNull("NpgsqlTypes.NpgsqlDateTime", type, value, ref isNull,
				(bt, o) => (DateTimeKind) bt.GetProperty("Kind").GetValue(value) == DateTimeKind.Unspecified);
		}

		static bool DynamicCheckForNull(Type type, object value, ref bool isNull)
		{
			return 
				DynamicCheckForNull("Oracle.DataAccess.Types.INullable",        type, value, ref isNull) ||
				DynamicCheckForNull("Oracle.ManagedDataAccess.Types.INullable", type, value, ref isNull) ||
				DynamicCheckForNull("IBM.Data.DB2Types.INullable",              type, value, ref isNull) ||
				DynamicCheckForNull("Sybase.Data.AseClient.AseDecimal",         type, value, ref isNull) ||
				DynamicCheckForNull("MySql.Data.Types.MySqlDecimal",            type, value, ref isNull) ||
				DynamicCheckForNull("MySql.Data.Types.MySqlDateTime",           type, value, ref isNull) ||
				DynamicCheckForNull("MySql.Data.Types.MySqlGeometry",           type, value, ref isNull) ||
				DynamicCheckNpgsqlDateTime(                                     type, value, ref isNull);
		}

		static bool IsNull(object value)
		{
			if (value == null || value is DBNull)
				return true;

			if (value is System.Data.SqlTypes.INullable nullable)
				return nullable.IsNull;

			var isNull = false;
			if (DynamicCheckForNull(value.GetType(), value, ref isNull))
				return isNull;
			return false;
		}

		static bool IsType(string baseType, Type type)
		{
			var baseTypeType = Type.GetType(baseType, false);

			if (baseTypeType == null || !baseTypeType.IsSameOrParentOf(type))
				return false;
			return true;
		}

		static bool IsValue(object value)
		{
			if (value == null)
				return false;
			var type = value.GetType();
			return
				value is System.Data.SqlTypes.INullable ||
				IsType("Oracle.DataAccess.Types.INullable", type) ||
				IsType("Oracle.ManagedDataAccess.Types.INullable", type) ||
				IsType("IBM.Data.DB2Types.INullable", type) ||
				IsType("MySql.Data.Types.MySqlGeometry", type);
		}

		static NumberFormatter GenerateNumberFormatter(Type valueType, Type innerType, bool checkForNull = true, Func<ParameterExpression, Expression> convertFunc = null)
		{
			//static NumberFormatter NF<T,TT>(Func<T,Func<TT,TT>> add, Func<TT,Func<int,object>> avr)

			// add
			// value => v => (v.IsNull ? 0 : v) + value
			var paramValue = Expression.Parameter(valueType);
			var paramV     = Expression.Parameter(innerType);
			var addLamba =
				Expression.Lambda(
					Expression.Lambda(
						Expression.Add(
							checkForNull
								? (Expression) Expression.Condition(Expression.PropertyOrField(paramV, "IsNull"),
									Expression.Constant(0, innerType), convertFunc != null ? convertFunc(paramV) : paramV)
								: paramV,
							paramValue
						), paramV
					), paramValue);

			// average
			// v => n => v / n

			var paramNumber = Expression.Parameter(typeof(int));
			var avgLambda =
				Expression.Lambda(
					Expression.Lambda(
						Expression.Divide(paramV, paramNumber),
						paramNumber
					), paramV
				);

			// format
			// v => new XElement("span", v)
			var constructor = typeof(XElement).GetConstructor(new []{typeof(string), innerType});
			if (constructor == null)
				constructor = typeof(XElement).GetConstructor(new []{typeof(string), typeof(object)});

			var formatExpr = Expression.Lambda(
				Expression.New(constructor ?? throw new InvalidOperationException(), Expression.Constant("span"), paramV)
			);

			var formatterType = typeof(NumberFormatter<,>).MakeGenericType(valueType, innerType);
			var formatter     = (NumberFormatter)Activator.CreateInstance(formatterType);

			formatterType.GetProperty("Add")    .SetValue(formatter, addLamba.  Compile());
			formatterType.GetProperty("Average").SetValue(formatter, avgLambda.   Compile());
			formatterType.GetProperty("Format") .SetValue(formatter, formatExpr.Compile());

			return formatter;
		}

		static ValueFormatter GetValueFormatter(Type type)
		{
			var vf = _valueFormatters.GetOrAdd(type, t =>
			{
				switch (type.FullName)
				{
					case "Oracle.DataAccess.Types.OracleDate":
					case "Oracle.ManagedDataAccess.Types.OracleDate":
					case "MySql.Data.Types.MySqlDateTime":
					case "IBM.Data.DB2Types.DB2DateTime":
					case "IBM.Data.DB2Types.DB2Date":
					case "IBM.Data.DB2Types.DB2Blob":
						return GenerateValueFormatter(type, dt => Expression.PropertyOrField(dt, "Value"));
					case "NpgsqlTypes.NpgsqlDateTime":
						return GenerateValueFormatter(type, dt => Expression.Convert(dt, typeof(DateTime)));
				}

				return null;
			});

			return vf;
//
			//VF       <Oracle.DataAccess.Types.OracleDate>    (dt => Format(dt.Value)),
			//VF<Oracle.ManagedDataAccess.Types.OracleDate>    (dt => Format(dt.Value)),
			//VF               <MySql.Data.Types.MySqlDateTime>(dt => Format(dt.Value)),
			//VF                   <NpgsqlTypes.NpgsqlDateTime>(dt => Format((DateTime)dt)),
			//VF                <IBM.Data.DB2Types.DB2DateTime>(dt => Format(dt.Value)),
			//VF                <IBM.Data.DB2Types.DB2Date>    (dt => Format(dt.Value)),

			//VF<IBM.Data.DB2Types.DB2Blob>(v => Format(v.Value)),
		}

		static NumberFormatter GetNumberFormatter(Type type)
		{
			var nf = _numberFormatters.GetOrAdd(type, t =>
			{

				try
				{
					switch (type.FullName)
					{
						case "Oracle.DataAccess.Types.OracleDecimal":
						case "Oracle.ManagedDataAccess.Types.OracleDecimal":
							return GenerateNumberFormatter(type, type, true);

						case "IBM.Data.DB2Types.DB2Int16":
						case "IBM.Data.DB2Types.DB2Int32":
						case "IBM.Data.DB2Types.DB2Int64":
							return GenerateNumberFormatter(type, typeof(Int64), true, p => Expression.PropertyOrField(p, "Value"));

						case "IBM.Data.DB2Types.DB2Decimal":
						case "IBM.Data.DB2Types.DB2DecimalFloat":
							return GenerateNumberFormatter(type, typeof(Decimal), true, p => Expression.PropertyOrField(p, "Value"));

						case "IBM.Data.DB2Types.DB2Double":
						case "IBM.Data.DB2Types.DB2Real":
							return GenerateNumberFormatter(type, typeof(Double), true, p => Expression.PropertyOrField(p, "Value"));

						case "Sybase.Data.AseClient.AseDecimal":
							return GenerateNumberFormatter(type, typeof(Double), true, p => Expression.Call(p, type.GetMethod("ToDouble")));

						case "Sap.Data.Hana.HanaDecimal":
							return GenerateNumberFormatter(type, typeof(Decimal), true, p => Expression.Call(p, type.GetMethod("ToDecimal")));

						case "MySql.Data.Types.MySqlDecimal":
							return GenerateNumberFormatter(type, typeof(Decimal), true, p => Expression.PropertyOrField(p, "Value"));
					}
				}
				catch (Exception)
				{
					// ignored
				}

				return null;
			});

			return nf;

			//NF<Oracle.DataAccess.       Types.OracleDecimal>(value => v => (v.IsNull ? 0 : v) + value, v => n => v / n),
			//NF<Oracle.ManagedDataAccess.Types.OracleDecimal>(value => v => (v.IsNull ? 0 : v) + value, v => n => v / n),

			//NF<IBM.Data.DB2Types.DB2Int16,       Int64>     (value => v => v + value.Value,            v => n => v / n),
			//NF<IBM.Data.DB2Types.DB2Int32,       Int64>     (value => v => v + value.Value,            v => n => v / n),
			//NF<IBM.Data.DB2Types.DB2Int64,       Int64>     (value => v => v + value.Value,            v => n => v / n),
			//NF<IBM.Data.DB2Types.DB2Decimal,     Decimal>   (value => v => v + value.Value,            v => n => v / n),
			//NF<IBM.Data.DB2Types.DB2DecimalFloat,Decimal>   (value => v => v + value.Value,            v => n => v / n),
			//NF<IBM.Data.DB2Types.DB2Double,      Double>    (value => v => v + value.Value,            v => n => v / n),
			//NF<IBM.Data.DB2Types.DB2Real,        Double>    (value => v => v + value.Value,            v => n => v / n),

			//NF<Sybase.Data.AseClient.AseDecimal, Double>    (value => v => v + value.ToDouble(),       v => n => v / n),
			//NF<Sap.Data.Hana.HanaDecimal,        Decimal>   (value => v => v + value.ToDecimal(),      v => n => v / n),

			//NF<MySql.Data.Types.MySqlDecimal,Decimal>       (value => v => v + value.Value,            v => n => v / n),
		}

		static XElement FormatValue(Total total, object value)
		{
			try
			{
				if (IsNull(value))
					return new XElement("td", new XAttribute("style", "text-align:center;"), new XElement("i", "null"));

				var nf = GetNumberFormatter(value.GetType());

				if (nf != null)
				{
					nf.AddTotal(total, value);
					return new XElement("td", new XAttribute("class", "n"), value);
				}

				var vf = GetValueFormatter(value.GetType());
				if (vf != null)
				{
					var list = new List<object>();

					if (vf.NoWrap)
						list.Add(new XAttribute("nowrap", "nowrap"));

					var style = "";

					if (vf.Font != null) style += $"font-family:{vf.Font};";
					if (vf.Size != null) style += $"font-size:{vf.Size};";

					if (style.Length > 0)
						list.Add(new XAttribute("style", style));

					list.Add(vf.FormatValue(value));

					return new XElement("td", list.ToArray());
				}

				return new XElement("td", value.ToString());
			}
			catch (Exception ex)
			{
				return new XElement("td",
					new XAttribute("style", "color:red"),
					ex.Message);
			}
		}

		public static object FormatValue(object value, ObjectGraphInfo info)
		{
			if (IsNull(value))
				return Util.RawHtml(new XElement("span", new XAttribute("style", "text-align:center;"), new XElement("i", "null")));

			//TODO: formatters
			//if (value is DB2Xml xml)
			//{
			//	var doc = XDocument.Parse(xml.GetString());
			//	return doc;
			//}

			if (_numberFormatters.TryGetValue(value.GetType(), out var nf))
				return Util.RawHtml(nf.GetElement(value));

			if (_valueFormatters.TryGetValue(value.GetType(), out var vf))
			{
				var style = "";

				if (vf.NoWrap) style += "white-space:nowrap;";
				if (vf.Font != null) style += $"font-family:{vf.Font};";
				if (vf.Size != null) style += $"font-size:{vf.Size};";

				return Util.RawHtml(new XElement("span",
					new XAttribute("style", style),
					vf.FormatValue(value)));
			}

			if (value is Microsoft.SqlServer.Types.SqlHierarchyId)
				return value.ToString();

			//Debug.WriteLine($"{value.GetType()}: {value} {IsValue(value)}");

			return IsValue(value) ? Util.RawHtml(new XElement("span", value)) : value;
		}

		static string Format(DateTime dt)
		{
			return dt.Hour == 0 && dt.Minute == 0 && dt.Second == 0
				? dt.ToString("yyyy-MM-dd")
				: dt.ToString("yyyy-MM-dd HH:mm:ss");
		}

		static string Format(byte[] value)
		{
			var sb = new StringBuilder($" Len:{value.Length} ");

			int i;

			for (i = 0; i < value.Length && i < 10; i++)
				sb.Append($"{value[i]:X2}:");

			if (i > 0)
				sb.Length--;

			if (i < value.Length)
				sb.Append("...");

			return sb.ToString();
		}

		//static ValueFormatter VF<T>(Func<T,string> format, string font = null, string size = null, bool nowrap = true)
		//{
		//	return new ValueFormatter<T> { Format = format, NoWrap = nowrap };
		//}

		static ValueFormatter GenerateValueFormatter(Type type, Func<ParameterExpression, Expression> dataExtractor, bool nowrap = true)
		{
			var param = Expression.Parameter(type);

			var extractedValue = dataExtractor(param);

			var methodInfo = extractedValue.Type != typeof(DateTime)
				? MethodHelper.GetMethodInfo(Format, new byte[0])
				: MethodHelper.GetMethodInfo(Format, DateTime.MaxValue);

			var expr = Expression.Lambda(
				Expression.Call(methodInfo, extractedValue),
				param);

			var formatterType = typeof(ValueFormatter<>).MakeGenericType(type);
			var formatter = (ValueFormatter)Activator.CreateInstance(formatterType);

			formatterType.GetProperty("Format").SetValue(formatter, expr.Compile());
			formatterType.GetProperty("NoWrap").SetValue(formatter, nowrap);

			return formatter;
		}

		static readonly ConcurrentDictionary<Type,ValueFormatter> _valueFormatters = new ConcurrentDictionary<Type, ValueFormatter>(new[]
		{
			VF<DateTime>                        (      Format),
			VF<System.Data.SqlTypes.SqlDateTime>(dt => Format(dt.Value)),
			VF<byte[]>(Format),
			VF<Guid>                            (v => v.      ToString("B").ToUpper(), font:"consolas", size:"110%"),
			VF<System.Data.SqlTypes.SqlGuid>    (v => v.Value.ToString("B").ToUpper(), font:"consolas", size:"110%"),

			// additional formatters are generated dynamically
		}
		.ToDictionary(f => f.Type));

		static readonly ConcurrentDictionary<Type,NumberFormatter> _numberFormatters = new ConcurrentDictionary<Type, NumberFormatter>(new[]
		{
			NF<Int16,  Int64>                               (value => v => v + value,                  v => n => v /       n),
			NF<Int32,  Int64>                               (value => v => v + value,                  v => n => v /       n),
			NF<Int64,  Int64>                               (value => v => v + value,                  v => n => v /       n),
			NF<UInt16, UInt64>                              (value => v => v + value,                  v => n => v / (uint)n),
			NF<UInt32, UInt64>                              (value => v => v + value,                  v => n => v / (uint)n),
			NF<UInt64, UInt64>                              (value => v => v + value,                  v => n => v / (uint)n),
			NF<SByte,  Int32>                               (value => v => v + value,                  v => n => v /       n),
			NF<Byte,   Int64>                               (value => v => v + value,                  v => n => v /       n),
			NF<Decimal,Decimal>                             (value => v => v + value,                  v => n => v /       n),
			NF<Double, Double>                              (value => v => v + value,                  v => n => v /       n),
			NF<Single, Single>                              (value => v => v + value,                  v => n => v /       n),

			NF<System.Data.SqlTypes.SqlInt16,Int64>         (value => v => v + value.Value,            v => n => v / n),
			NF<System.Data.SqlTypes.SqlInt32,Int64>         (value => v => v + value.Value,            v => n => v / n),
			NF<System.Data.SqlTypes.SqlInt64,Int64>         (value => v => v + value.Value,            v => n => v / n),
			NF<System.Data.SqlTypes.SqlByte, Int64>         (value => v => v + value.Value,            v => n => v / n),
			NF<System.Data.SqlTypes.SqlDecimal>             (value => v => (v.IsNull ? 0 : v) + value, v => n => v / n),
			NF<System.Data.SqlTypes.SqlDouble>              (value => v => (v.IsNull ? 0 : v) + value, v => n => v / n),
			NF<System.Data.SqlTypes.SqlSingle>              (value => v => (v.IsNull ? 0 : v) + value, v => n => v / n),

			//Dynamic types will be genareted later

		}
		.ToDictionary(f => f.Type));

		#region IsNullChecker

		private static readonly ConcurrentDictionary<Type, IsNullChecker> _isNullCheckers = new ConcurrentDictionary<Type, IsNullChecker>();

		abstract class IsNullChecker
		{
			public abstract bool IsNull(object value);
		}

		class IsNullChecker<T> : IsNullChecker
		{
			private readonly Func<T,bool> _isNullFunc;

			public IsNullChecker(Func<T,bool> isNullFunc)
			{
				_isNullFunc = isNullFunc;
			}

			public override bool IsNull(object value)
			{
				return _isNullFunc((T) value);
			}
		}

		#endregion

		#region ValueFormatter

		static ValueFormatter VF<T>(Func<T,string> format, string font = null, string size = null, bool nowrap = true)
		{
			return new ValueFormatter<T> { Format = format, NoWrap = nowrap };
		}

		abstract class ValueFormatter
		{
			public abstract Type   Type { get; }

// Warning CS0649  Field 'XmlFormatter.ValueFormatter.Font' is never assigned to, and will always have its default value null
#pragma warning disable 649
			public string Font;
			public string Size;
#pragma warning restore 649
			public bool   NoWrap;

			public abstract string FormatValue(object value);
		}

		class DynamicFormatter : ValueFormatter
		{
			private readonly Func<object, string> _formatFunc;

			public DynamicFormatter(Type type, Func<object, string> formatFunc)
			{
				Type = type;
				_formatFunc = formatFunc;
			}

			public override Type Type { get; }

			public override string FormatValue(object value)
			{
				return (string) _formatFunc.DynamicInvoke(value);
			}
		}

		class ValueFormatter<T> : ValueFormatter
		{
			public override Type Type => typeof(T);

			public Func<T,string> Format;

			public override string FormatValue(object value)
			{
				return Format((T)value);
			}
		}

		#endregion

		#region NumberFormatter

		static NumberFormatter NF<T>(Func<T,Func<T,T>> add, Func<T,Func<int,object>> avr)
		{
			return new NumberFormatter<T> { Add = add, Avarege = avr, Format = v => new XElement("span", v) };
		}

		static NumberFormatter NF<T,TT>(Func<T,Func<TT,TT>> add, Func<TT,Func<int,object>> avr)
		{
			return new NumberFormatter<T,TT> { Add = add, Avarege = avr, Format = v => new XElement("span", v) };
		}

		abstract class NumberFormatter
		{
			public abstract Type Type { get; }

			public abstract void     AddTotal  (Total total, object value);
			public abstract XElement GetElement(object value);
		}

		class NumberFormatter<T> : NumberFormatter
		{
			public override Type Type => typeof(T);

			public Func<T,Func<T,T>>        Add;
			public Func<T,Func<int,object>> Avarege;

			public override void AddTotal(Total total, object value)
			{
				total.Add(Add((T)value), Avarege);
			}

			public Func<T,XElement> Format;

			public override XElement GetElement(object value)
			{
				return Format((T)value);
			}
		}

		class NumberFormatter<T,TT> : NumberFormatter
		{
			public override Type Type => typeof(T);

			public Func<T,Func<TT,TT>>       Add;
			public Func<TT,Func<int,object>> Avarege;

			public override void AddTotal(Total total, object value)
			{
				total.Add<TT>(Add((T)value), Avarege);
			}

			public Func<T,XElement> Format;

			public override XElement GetElement(object value)
			{
				return Format((T)value);
			}
		}

		#endregion
	}
}
