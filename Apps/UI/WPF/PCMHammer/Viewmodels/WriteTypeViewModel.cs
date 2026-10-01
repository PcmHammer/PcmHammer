using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcmHacking;
using PCMHammer.Helpers;
using System.Collections.ObjectModel;

namespace PCMHammer.Viewmodels
{
    public partial class WriteTypeViewModel : ObservableObject
    {
        public ObservableCollection<WriteType> WriteTypes { get; }
        public ObservableCollection<PcmType> PCMTypes { get; }

        #region Properties
        [ObservableProperty]
        public partial WriteType SelectedWriteType { get; set; }

        [ObservableProperty]
        public partial PcmType SelectedPCMType { get; set; }

        #endregion

        // Events
        public event Action? RequestClose;
        public event Action? RequestAcceptandClose;

        // Commands. Do NOT name these methods "...Command": the generator appends "Command" to the
        // method name, so CloseCommand() would produce CloseCommandCommand and the XAML binding to
        // CloseCommand would silently resolve to nothing (which is why OK did nothing - Cancel only
        // appeared to work because IsCancel="True" closes the dialog by itself).
        [RelayCommand]
        public void Close() => RequestClose?.Invoke();
        [RelayCommand]
        public void AcceptAndClose() => RequestAcceptandClose?.Invoke();

        // Constructor
        /// <param name="detected">
        /// The PCM found by probing the bus before the dialog opened, or null if nothing was detected.
        /// Decides which write types are offered; with nothing detected they all are, and the writer
        /// rejects any the PCM turns out not to support.
        /// </param>
        public WriteTypeViewModel(OSIDInfo? detected = null)
        {
            OperationOptions options = OperationOptions.For(detected);
            WriteTypes = [.. options.WriteTypes];

            PCMTypes = [.. Enum.GetValues<PcmType>()];

            // Seed the selections here rather than with ComboBox.SelectedIndex in the view. Setting
            // SelectedIndex right after DataContext is timing-dependent: if ItemsSource has not been
            // populated at that instant the assignment silently does nothing, SelectedIndex stays -1,
            // and these properties keep their CLR defaults. For WriteType that default is
            // WriteType.None (= 0), which is not a real operation - it survived all the way into
            // CanKernelWriter and threw "Unsuppported operation type: None" only AFTER the kernel had
            // been uploaded and was running on the PCM.
            // Default from the shared policy (OperationOptions.PreferredWriteType): a full clone when
            // "force write all sectors" is on, otherwise calibration - falling back to the first
            // offered type (Clone) if the preferred one is not available for this PCM. Yields
            // WriteType.None only for a read-only PCM, where no write type is offered at all.
            SelectedWriteType = options.PreferredWriteType(WritePlan.DefaultWriteType());
            SelectedPCMType = PcmType.Undefined; // Auto (Query OSID)
        }
    }
}
