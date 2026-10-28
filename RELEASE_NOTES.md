fb1d66c adjust release workflow
9d808f4 fix(SteamBBCodeViewer): critical buffer bug in ParseInline (pending not cleared before formatted tags [b]/[i]/[u]/[s]); images as independent StackPanel blocks instead of inline in TextBlock; normalize \r\n / \r / \u00A0 to \n with blank-line paragraph split for correct Steam description layout
8ddab40 fix(browser): detect server certificate error via ServerCertificateErrorDetected; UI micro-adjusts placeholder contrast + IsSecureReal wrapper
9df4c31 Fix browser nav dialog: remove external-site confirmation (Steam handles it)
