;; Optional portable/developer entry point. Normal installation uses Autodesk's
;; version-aware PackageContents.xml and the native CS16 command directly.
(defun cs16:family (raw / version)
  (setq version (atof raw))
  (cond ((equal version 23.1 0.0001) "2020")
        ((and (>= version 24.0) (<= version 24.3)) "2021-2024")
        ((and (>= version 25.0) (<= version 25.1)) "2025-2026")
        ((equal version 26.0 0.0001) "2027")
        (T nil)))
(defun cs16:launch (bundle / family dll)
  (setq family (cs16:family (getvar "ACADVER")))
  (cond
    ((not family) (princ "\nCS16: supported AutoCAD versions are 2020-2027, Windows x64.\n"))
    ((not (setq dll (findfile (strcat bundle "/Contents/" family "/AutoCADCS.dll"))))
      (princ "\nCS16: module missing. Extract the complete CS16.bundle folder.\n"))
    (T
      (if (not (getcname "CS16V110")) (command "_.NETLOAD" dll))
      (if (getcname "CS16V110")
        (command "CS16V110")
        (princ "\nCS16: module could not be loaded. See the AutoCAD command history.\n"))))
  (princ))
(princ)
