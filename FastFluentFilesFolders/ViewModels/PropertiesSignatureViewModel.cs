using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace FastFluentFilesFolders.ViewModels
{
	/// <summary>数字签名页 ViewModel：嵌入签名 / 目录签名。</summary>
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
				new SignatureRow(App.ML.Get("PropertiesNoCatalogSignatures"), "sha256", "—")
			};
		}

		private static List<SignatureRow> BuildEmbedded(FileSystemNodeViewModel item, out List<string> details)
		{
			details = new List<string>();
			var rows = new List<SignatureRow>();
			if (item.IsDirectory)
			{
				rows.Add(new SignatureRow(App.ML.Get("PropertiesNoSignature"), "sha256", "—"));
				return rows;
			}

			try
			{
				using var cert = X509Certificate.CreateFromSignedFile(item.FullPath);
				if (cert == null)
				{
					rows.Add(new SignatureRow(App.ML.Get("PropertiesNoSignature"), "sha256", "—"));
					return rows;
				}

				using var cert2 = new X509Certificate2(cert);
				var signer = cert2.Subject;
				rows.Add(new SignatureRow(signer, "sha256", "—"));
				details.Add($"{App.ML.Get("PropertiesSignatureSubject")} {signer}");
				details.Add($"{App.ML.Get("PropertiesSignatureIssuer")} {cert2.Issuer}");
				details.Add($"{App.ML.Get("PropertiesSignatureSerial")} {cert2.SerialNumber}");
				details.Add($"{App.ML.Get("PropertiesSignatureValidFrom")} {cert2.NotBefore:yyyy-MM-dd HH:mm:ss}");
				details.Add($"{App.ML.Get("PropertiesSignatureValidTo")} {cert2.NotAfter:yyyy-MM-dd HH:mm:ss}");
				details.Add($"{App.ML.Get("PropertiesSignatureThumbprint")} {cert2.Thumbprint}");
				details.Add($"{App.ML.Get("PropertiesSignatureAlgorithm")} {cert2.SignatureAlgorithm.FriendlyName}");
			}
			catch (Exception ex)
			{
				rows.Add(new SignatureRow(App.ML.Get("PropertiesNoSignature"), "sha256", "—"));
				details.Add($"({ex.Message})");
			}
			return rows;
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
