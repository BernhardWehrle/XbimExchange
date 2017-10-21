﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Xbim.WindowsUI.DPoWValidation.ViewModels;

namespace Xbim.WindowsUI.DPoWValidation.Commands
{

    public class ValidateCommand : ICommand
    {
        private ValidationViewModel _vm;

        public bool CanExecute(object parameter)
        {
            return _vm.RequirementFileInfo.Exists
                && _vm.SubmissionFileInfo.Exists
                && _vm.ReportFileInfo.IsValidName(Models.SourceFile.AllowedExtensions.Cobie);
        }

        public event EventHandler CanExecuteChanged;
        

        public ValidateCommand(ValidationViewModel validationViewModel)
        {
            _vm = validationViewModel;
        }

        public void ChangesHappened()
        {
            if (CanExecuteChanged != null)
            {
                CanExecuteChanged.Invoke(this, new EventArgs());
            }
        }

        public void Execute(object parameter)
        {
            _vm.ExportOnValidated = false;
            _vm.OpenOnExported = false;
            _vm.ExecuteValidation();
        }
    }
}
