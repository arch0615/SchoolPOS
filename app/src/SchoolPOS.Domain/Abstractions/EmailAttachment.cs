namespace SchoolPOS.Domain.Abstractions;

/// <summary>Adjunto de un correo saliente (p. ej. el XML/PDF de un CFDI recién timbrado).</summary>
public sealed record EmailAttachment(string FileName, byte[] Content, string ContentType);
