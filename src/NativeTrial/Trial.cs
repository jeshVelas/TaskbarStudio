namespace NativeTrial;

internal interface ITaskbar
{
    bool Read();
    nuint Set(bool enabled);
}

// The journal records intent before the first Shell write. Keep it on any error.
internal sealed class Trial(ITaskbar taskbar, string directory)
{
    private readonly string journal = Path.Combine(directory, "pending.v1");
    public bool? Original
    {
        get
        {
            try
            {
                return File.ReadAllText(journal) switch
                {
                    "v1:off" => false,
                    "v1:on" => true,
                    _ => throw new InvalidDataException("Registro de recuperación ilegible. Puede conservar el estado actual.")
                };
            }
            catch (FileNotFoundException) { return null; }
        }
    }

    public void Apply(bool target)
    {
        if (Original.HasValue)
            throw new InvalidOperationException("Resuelva primero la prueba pendiente.");
        bool original = taskbar.Read();
        if (original == target) return;

        string staging = journal + ".tmp";
        using (var stream = new FileStream(staging, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(original ? "v1:on"u8 : "v1:off"u8);
            stream.Flush(true);
        }
        File.Move(staging, journal);
        if (taskbar.Read() != original)
            throw new InvalidOperationException("El estado cambió antes de aplicar la prueba.");
        SetAndVerify(target);
    }

    public void Restore()
    {
        if (Original is not bool original) return;
        if (taskbar.Read() != original)
            SetAndVerify(original);
        Keep();
    }

    public void Keep() => File.Delete(journal);

    private void SetAndVerify(bool target)
    {
        nuint result = taskbar.Set(target);
        bool observed = taskbar.Read();
        if (observed != target)
            throw new InvalidOperationException($"ABM_SETSTATE devolvió 0x{result:X}. " +
                $"ABS_AUTOHIDE solicitado={(target ? 1 : 0)}, leído={(observed ? 1 : 0)}. Se conserva el registro.");
    }
}
