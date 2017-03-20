﻿using System;
using AnylineXamarinSDK.iOS;
using CoreGraphics;
using Foundation;
using UIKit;

namespace AnylineXamarinApp.iOS.Modules.OCR.ViewController
{
    public sealed class AnylineVoucherScanViewController : UIViewController, IAnylineOCRModuleDelegate
    {
        readonly string _licenseKey = AnylineViewController.LicenseKey;

        AnylineOCRModuleView _scanView;
        ResultOverlayView _resultView;

        ALOCRConfig _ocrConfig;

        NSError _error;
        bool _success;
        bool _isScanning;
        public UIAlertView Alert { get; private set; }

        public AnylineVoucherScanViewController(string name)
        {
            Title = name;
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            // Initializing the Voucher Code module.
            CGRect frame = UIScreen.MainScreen.ApplicationFrame;
            frame = new CGRect(frame.X,
                frame.Y + NavigationController.NavigationBar.Frame.Size.Height,
                frame.Width,
                frame.Height - NavigationController.NavigationBar.Frame.Size.Height);

            _scanView = new AnylineOCRModuleView(frame);

            // We'll define the OCR Config here:
            _ocrConfig = new ALOCRConfig();
            _ocrConfig.CharHeight = new ALRange { min = 45, max = 85 };
            _ocrConfig.TesseractLanguages = new[] { @"anyline_capitals" };
            _ocrConfig.CharWhiteList = "ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
            _ocrConfig.MinConfidence = 85;
            _ocrConfig.ScanMode = ALOCRScanMode.Grid;
            _ocrConfig.CharCountX = 8;
            _ocrConfig.CharCountY = 1;
            _ocrConfig.CharPaddingXFactor = 0.5;
            _ocrConfig.ValidationRegex = "[A-Z0-9]{8}$";
            _ocrConfig.RemoveSmallContours = true;
            _ocrConfig.IsBrightTextOnDark = true;

            // Experimental parameter to set the minimum sharpness (value between 0-100; 0 to turn sharpness detection off)
            // The goal of the minimum sharpness is to avoid a time consuming ocr step,
            // if the image is blurry and good results are therefore not likely.
            _ocrConfig.MinSharpness = 50;

            // We tell the module to bootstrap itself with the license key and delegate. The delegate will later get called
            // by the module once we start receiving results.
            _error = null;
            _success = _scanView.SetupWithLicenseKey(_licenseKey, Self, _ocrConfig, out _error);
            // SetupWithLicenseKey:delegate:error returns true if everything went fine. In the case something wrong
            // we have to check the error object for the error message.
            if (!_success)
            {
                // Something went wrong. The error object contains the error description
                (Alert = new UIAlertView("Error", _error.DebugDescription, null, "OK", null)).Show();
            }

            // We stop scanning manually
            _scanView.CancelOnResult = false;

            // We load the UI config for our VoucherCode view from a .json file.
            String configFile = NSBundle.MainBundle.PathForResource(@"Modules/OCR/voucher_code_config", @"json");
            _scanView.CurrentConfiguration = ALUIConfiguration.CutoutConfigurationFromJsonFile(configFile);
            _scanView.TranslatesAutoresizingMaskIntoConstraints = false;

            // After setup is complete we add the module to the view of this view controller
            View.AddSubview(_scanView);

            /*
             The following view will present the scanned values. Here we start listening for taps
             to later dismiss the view.
             */
            _resultView = new ResultOverlayView(new CGRect(0, 0, View.Frame.Width, View.Frame.Height), UIImage.FromBundle(@"drawable/gift_card_background.png"));
            _resultView.AddGestureRecognizer(new UITapGestureRecognizer(this, new ObjCRuntime.Selector("ViewTapSelector:")));

            _resultView.Center = View.Center;
            _resultView.Alpha = 0;

            _resultView.Result.Center = new CGPoint(View.Center.X, View.Center.Y - 45);
            _resultView.Result.Font = UIFont.BoldSystemFontOfSize(18);
            _resultView.Result.TextColor = UIColor.White;

            View.AddSubview(_resultView);
        }

        /*
         Dismiss the view if the user taps the screen
         */
        [Export("ViewTapSelector:")]
        private void AnimateFadeOut(UIGestureRecognizer sender)
        {
            _resultView.AnimateFadeOut(View, StartAnyline);
        }

        /*
         This method will be called once the view controller and its subviews have appeared on screen
         */
        public override void ViewDidAppear(bool animated)
        {
            base.ViewDidAppear(animated);

            // We use this subroutine to start Anyline. The reason it has its own subroutine is
            // so that we can later use it to restart the scanning process.
            StartAnyline();
        }

        public void StartAnyline()
        {
            if (_isScanning) return;

            //send the result view to the back before we start scanning
            View.SendSubviewToBack(_resultView);

            _error = null;
            _success = _scanView.StartScanningAndReturnError(out _error);
            if (!_success)
            {
                (Alert = new UIAlertView("Error", _error.DebugDescription, null, "OK", null)).Show();
            }
            else
                _isScanning = true;
        }

        public void StopAnyline()
        {
            if (!_isScanning) return;

            _error = null;
            if (!_scanView.CancelScanningAndReturnError(out _error))
            {
                (Alert = new UIAlertView("Error", _error.DebugDescription, null, "OK", null)).Show();
            }
            else
                _isScanning = false;

            View.BringSubviewToFront(_resultView);
        }

        public override void ViewWillDisappear(bool animated)
        {
            base.ViewWillDisappear(animated);

            // We have to stop scanning before the view dissapears
            StopAnyline();
        }

        public override void ViewDidDisappear(bool animated)
        {
            base.ViewDidDisappear(animated);

            //remove result view
            _resultView?.RemoveFromSuperview();
            _resultView?.Dispose();
            
            //we have to erase the scan view so that there are no dependencies for the viewcontroller left.
            _scanView?.RemoveFromSuperview();
            _scanView?.Dispose();
            
            Dispose();
        }


        /*
        This is the main delegate method Anyline uses to report its results
        */
        void IAnylineOCRModuleDelegate.DidFindResult(AnylineOCRModuleView anylineOCRModuleView, ALOCRResult result)
        {

            StopAnyline();
            View.BringSubviewToFront(_resultView);

            _resultView.UpdateResult(result.Text);

            // Present the information to the user
            _resultView.AnimateFadeIn(View);
        }

        void IAnylineOCRModuleDelegate.ReportsRunFailure(AnylineOCRModuleView anylineOCRModuleView, ALOCRError error)
        {
            switch (error)
            {
                case ALOCRError.ConfidenceNotReached:
                    Console.WriteLine("Confidence not reached.");
                    break;
                case ALOCRError.NoLinesFound:
                    Console.WriteLine("No lines found.");
                    break;
                case ALOCRError.NoTextFound:
                    Console.WriteLine("No text found.");
                    break;
                case ALOCRError.ResultNotValid:
                    Console.WriteLine("Result is not valid.");
                    break;
                case ALOCRError.SharpnessNotReached:
                    Console.WriteLine("Sharpness is not reached.");
                    break;
                case ALOCRError.Unkown:
                    Console.WriteLine("Unknown run error.");
                    break;
            }
        }

        void IAnylineOCRModuleDelegate.ReportsVariable(AnylineOCRModuleView anylineOCRModuleView, string variableName, NSObject value) { }

        bool IAnylineOCRModuleDelegate.TextOutlineDetected(AnylineOCRModuleView anylineOCRModuleView, ALSquare outline) { return false; }
    }
}