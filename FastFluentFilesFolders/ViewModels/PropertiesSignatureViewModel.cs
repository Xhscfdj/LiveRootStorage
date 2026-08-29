using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace FastFluentFilesFolders.ViewModels
{
	/// <summary>数字签名页 ViewModel：嵌入签名 / 目录签名（含时间戳与摘要算法）。</summary>
	public partial class PropertiesSignatureViewModel : ObservableObject
	{
		public string EmbeddedHeader => App.ML.Get("PropertiesEmbeddedSignatures");
		public string CatalogHeader => App.ML.Get("PropertiesCatalogSignatures");
		public string SignerColumnHeader => App.ML.Get("PropertiesSignatureColumnSigner");
		public string DigestColumnHeader => App.ML.Get("PropertiesSignatureColumnDigest");
		public string TimestampColumnHeader => App.ML.Get("PropertiesSignatureColumnTimestamp");
		public string DetailsButtonLabel => App.ML.Get("PropertiesSignatureDetailsButton");

		public IReadOnlyList<SignatureRow> EmbeddedRows { get; }
		public IReadOnlyList<SignatureRow> CatalogRows { get; }
		public IReadOnlyList<string> EmbeddedDetails { get; }
		public bool HasEmbeddedDetails { get; }

		public PropertiesSignatureViewModel(FileSystemNodeViewModel item)
		{
			EmbeddedRows = BuildEmbedded(item, out var details);
			EmbeddedDetails = details;
			HasEmbeddedDetails = details.Count > 0;
			CatalogRows = new List<SignatureRow>
			{
				new SignatureRow(App.ML.Get("PropertiesNoCatalogSignatures"), string.Empty, string.Empty)
			};
		}

		private static List<SignatureRow> BuildEmbedded(FileSystemNodeViewModel item, out List<string> details)
		{
			details = new List<string>();
			var rows = new List<SignatureRow>();
			if (item.IsDirectory)
			{
				rows.Add(new SignatureRow(App.ML.Get("PropertiesNoSignature"), string.Empty, string.Empty));
				return rows;
			}

			try
			{
				using var cert = X509Certificate.CreateFromSignedFile(item.FullPath);
				if (cert == null)
				{
					rows.Add(new SignatureRow(App.ML.Get("PropertiesNoSignature"), string.Empty, string.Empty));
					return rows;
				}

				using var cert2 = new X509Certificate2(cert);
				var signer = cert2.Subject;

				// 从内嵌 PKCS#7 提取摘要算法与签名时间戳（支持旧式计数器签名与 RFC3161）
				TryGetSignatureInfo(item.FullPath, out var digest, out var timestampUtc);
				var digestText = string.IsNullOrEmpty(digest) ? "-" : digest;
				var timestampText = timestampUtc.HasValue
					? timestampUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
					: "-";

				rows.Add(new SignatureRow(signer, digestText, timestampText));
				details.Add($"{App.ML.Get("PropertiesSignatureSubject")} {signer}");
				details.Add($"{App.ML.Get("PropertiesSignatureIssuer")} {cert2.Issuer}");
				details.Add($"{App.ML.Get("PropertiesSignatureSerial")} {cert2.SerialNumber}");
				details.Add($"{App.ML.Get("PropertiesSignatureValidFrom")} {cert2.NotBefore:yyyy-MM-dd HH:mm:ss}");
				details.Add($"{App.ML.Get("PropertiesSignatureValidTo")} {cert2.NotAfter:yyyy-MM-dd HH:mm:ss}");
				details.Add($"{App.ML.Get("PropertiesSignatureThumbprint")} {cert2.Thumbprint}");
				details.Add($"{App.ML.Get("PropertiesSignatureAlgorithm")} {cert2.SignatureAlgorithm.FriendlyName}");
				if (!string.IsNullOrEmpty(digest))
					details.Add($"{App.ML.Get("PropertiesSignatureDigest")} {digest}");
				if (timestampUtc.HasValue)
					details.Add($"{App.ML.Get("PropertiesSignatureTimestamp")} {timestampText}");
			}
			catch (Exception ex)
			{
				rows.Add(new SignatureRow(App.ML.Get("PropertiesNoSignature"), string.Empty, string.Empty));
				details.Add($"({ex.Message})");
			}
			return rows;
		}

		// ------------------------------------------------------------ 签名解析

		/// <summary>从 PE 文件的证书表提取内嵌 PKCS#7 数据。</summary>
		private static byte[]? ExtractEmbeddedPkcs7(string path)
		{
			try
			{
				using var fs = File.OpenRead(path);
				using var br = new BinaryReader(fs);

				fs.Position = 0x3C;                       // e_lfanew
				var peOffset = br.ReadUInt32();
				fs.Position = peOffset;
				if (br.ReadUInt32() != 0x00004550) return null; // "PE\0\0"

				// IMAGE_FILE_HEADER (20 字节)
				br.ReadUInt16(); br.ReadUInt16(); br.ReadUInt32();
				br.ReadUInt32(); br.ReadUInt32(); br.ReadUInt16(); br.ReadUInt16();

				// IMAGE_OPTIONAL_HEADER：按 Magic 定位 DataDirectory 起始偏移
				var optStart = fs.Position;
				var magic = br.ReadUInt16();
				var dataDirOff = magic switch
				{
					0x10B => (int)optStart + 96,   // PE32
					0x20B => (int)optStart + 112,  // PE32+
					_ => -1
				};
				if (dataDirOff < 0) return null;

				// DataDirectory[4] = 证书表（{ RVA(文件偏移), Size }）
				fs.Position = dataDirOff + 4 * 8;
				var certRva = br.ReadUInt32();
				var certSize = br.ReadUInt32();
				if (certRva == 0 || certSize == 0) return null;

				// WIN_CERTIFICATE：dwLength, wRevision, wCertificateType, bCertificate
				fs.Position = certRva;
				var dwLength = br.ReadUInt32();
				br.ReadUInt16(); br.ReadUInt16();
				var pkcsLen = dwLength - 8;
				if (pkcsLen <= 0) return null;
				return br.ReadBytes((int)pkcsLen);
			}
			catch { return null; }
		}

		/// <summary>解析摘要算法与签名时间戳（旧式计数器签名 / RFC3161）。</summary>
		private static bool TryGetSignatureInfo(string path, out string digest, out DateTime? timestampUtc)
		{
			digest = string.Empty;
			timestampUtc = null;
			var pkcs7 = ExtractEmbeddedPkcs7(path);
			if (pkcs7 == null) return false;

			try
			{
				var cms = new SignedCms();
				cms.Decode(pkcs7);
				var si = cms.SignerInfos[0];
				digest = NormalizeDigest(si.DigestAlgorithm.FriendlyName);

				// 1) 旧式计数器签名（signingTime 1.2.840.113549.1.9.5）
				foreach (var cs in si.CounterSignerInfos)
				{
					foreach (var a in cs.SignedAttributes)
					{
						if (a.Oid.Value == "1.2.840.113549.1.9.5")
						{
							var st = new Pkcs9SigningTime(a.Values[0].RawData);
							timestampUtc = st.SigningTime;
							return true;
						}
					}
				}

				// 2) RFC3161 时间戳（1.3.6.1.4.1.311.3.3.1，token 内 signingTime）
				foreach (var attr in si.UnsignedAttributes)
				{
					if (attr.Oid.Value != "1.3.6.1.4.1.311.3.3.1") continue;
					foreach (var v in attr.Values)
					{
						try
						{
							var tok = new SignedCms();
							tok.Decode(v.RawData);
							var tsi = tok.SignerInfos[0];
							foreach (var a in tsi.SignedAttributes)
							{
								if (a.Oid.Value == "1.2.840.113549.1.9.5")
								{
									var st = new Pkcs9SigningTime(a.Values[0].RawData);
									timestampUtc = st.SigningTime;
									return true;
								}
							}
						}
						catch { }
					}
				}
				return true;
			}
			catch { return false; }
		}

		private static string NormalizeDigest(string? friendly)
		{
			var f = friendly?.ToUpperInvariant() ?? string.Empty;
			if (f.Contains("SHA256")) return "SHA256";
			if (f.Contains("SHA384")) return "SHA384";
			if (f.Contains("SHA512")) return "SHA512";
			if (f.Contains("SHA1")) return "SHA1";
			if (f.Contains("MD5")) return "MD5";
			return string.IsNullOrEmpty(friendly) ? "-" : friendly;
		}
	}

	/// <summary>签名表格的一行。</summary>
	public sealed class SignatureRow
	{
		public string Signer { get; }
		public string Digest { get; }
		public string Timestamp { get; }

		public SignatureRow(string signer, string digest, string timestamp)
		{
			Signer = signer;
			Digest = digest;
			Timestamp = timestamp;
		}
	}
}