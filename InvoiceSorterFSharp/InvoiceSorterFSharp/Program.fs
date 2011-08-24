open System
open System.Windows
open System.Windows.Markup
open System.Windows.Controls
open System.IO
open System.ComponentModel

open iTextSharp
open iTextSharp.text
open iTextSharp.text.pdf

let loadXaml<'T when 'T :> FrameworkElement>(xamlPath) =
    use stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(xamlPath) // if BuildAction=EmbeddedResource
#if SILVERLIGHT
    let stream = (new System.IO.StreamReader(stream)).ReadToEnd()
#endif
    let xaml = System.Windows.Markup.XamlReader.Load(stream)
    let uiObj = xaml :?> 'T
    uiObj
in

let app = new Application()
let window: Window = loadXaml("InvoiceSorterFSharp.xaml")

let invoiceFileList = window.FindName("invoiceFileList") :?> ListBox
invoiceFileList.Items.SortDescriptions.Add(new SortDescription("Content", ListSortDirection.Ascending)) |> ignore

let detailFileList = window.FindName("detailFileList") :?> ListBox
detailFileList.Items.SortDescriptions.Add(new SortDescription("Content", ListSortDirection.Ascending)) |> ignore

let mainMenuExitClickHandler (evArgs: RoutedEventArgs) = 
    let sender = evArgs.OriginalSource :?> MenuItem
    app.Shutdown(0) |> ignore

let mainMenuExit = window.FindName("mainMenuExit") :?> MenuItem
mainMenuExit.Click.Add(mainMenuExitClickHandler)

let selectorClickHandler (evArgs: RoutedEventArgs) = 
    let sender = evArgs.OriginalSource :?> Button
    let fileDialog = new Forms.OpenFileDialog()
    fileDialog.Multiselect <- true
    let listControlName =
        if sender.Name.Equals("invoiceSelector") then
            fileDialog.Filter <- "*.pdf|*.PDF"
            "invoiceFileList"
        else
            fileDialog.Filter <- "*.dat|*.DAT"
            "detailFileList"
    let listControl = window.FindName(listControlName) :?> ListBox
    listControl.Items.Clear()
    if fileDialog.ShowDialog() = Forms.DialogResult.OK then
        for fileName in fileDialog.FileNames do
            let item = new ListBoxItem()
            item.Content <- fileName
            listControl.Items.Add(item) |> ignore

let invoiceSelector = (window.FindName("invoiceSelector") :?> Button)
invoiceSelector.Click.Add(selectorClickHandler)
let detailSelector = (window.FindName("detailSelector") :?> Button)
detailSelector.Click.Add(selectorClickHandler)

let outputFolderSelectorClickHandler(evArgs: RoutedEventArgs) = 
    let sender = evArgs.OriginalSource :?> Button
    let folderDialog = new Forms.FolderBrowserDialog()
    if folderDialog.ShowDialog() = Forms.DialogResult.OK then
        (window.FindName("outputFolder") :?> TextBox).Text <- folderDialog.SelectedPath
    else
        ()

let outputFolderSelector = (window.FindName("outputFolderSelector") :?> Button)
outputFolderSelector.Click.Add(outputFolderSelectorClickHandler)

let convertFile (evArgs: RoutedEventArgs) =
    let outputFolder = (window.FindName("outputFolder") :?> TextBox)
    if outputFolder.Text.Equals("") then
        MessageBox.Show("저장할 폴더를 선택하세요") |> ignore
    else
        let outputPdfFile = outputFolder.Text + Path.DirectorySeparatorChar.ToString() + "out.pdf"
        let outputDatFile = outputFolder.Text + Path.DirectorySeparatorChar.ToString() + "out.dat"

        let invoicePosition1 = (window.FindName("invoicePosition1") :?> TextBox).Text
        let invoicePosition2 = (window.FindName("invoicePosition2") :?> TextBox).Text
        let detailPosition1 = (window.FindName("detailPosition1") :?> TextBox).Text
        let detailPosition2 = (window.FindName("detailPosition2") :?> TextBox).Text

        if invoiceFileList.Items.Count > 0 then
            let invoiceFileArray =
                Array.init invoiceFileList.Items.Count (fun x -> ((invoiceFileList.Items.GetItemAt(x) :?> ListBoxItem).Content :?> String))
            let invoiceFileNameList = Array.toList(invoiceFileArray)
            let aux1 (fileName: String) =
                let file = new StreamReader(new FileStream(fileName.Replace(".pdf", ".dat"), FileMode.Open), Text.Encoding.GetEncoding("euc-kr"))
                let reader = new PdfReader(fileName)
                let rec aux2 item page resultList =
                    if file.EndOfStream then
                        resultList
                    else
                        let line = file.ReadLine()
                        if not (line.IndexOf("%%Page:") = -1) then
                            let page = Int32.Parse(line.Split([|' '|]).[1])
                            aux2 "" page resultList
                        elif not (line.IndexOf("showpage") = -1) then
                            if not (page = 0) then
                                aux2 "" page ((item, page, ref reader) :: resultList)
                            else
                                aux2 "" page resultList
                        elif not (line.IndexOf(invoicePosition1) = -1) then
                            let item = line + item
                            aux2 item page resultList
                        elif not (invoicePosition2.Equals("")) && not (line.IndexOf(invoicePosition2) = -1) then
                            let item = item + line
                            aux2 item page resultList
                        else
                            aux2 item page resultList
                in
                let result = aux2 "" 0 []
                file.Close()
                result
            in
            let sortedList = List.sortBy (fun (item, _, _) -> item) (List.concat (List.map aux1 invoiceFileNameList))
            match sortedList with
            | [] -> ()
            | (_, _, reader: PdfReader ref) as head :: tail ->
                let document = new Document((!reader).GetPageSize(1))
                let writer = PdfWriter.GetInstance(document, new FileStream(outputPdfFile, FileMode.Create))
                document.Open()
                List.map (
                    fun (_, page, reader: PdfReader ref) -> 
                        let cb = writer.DirectContent
                        let page = writer.GetImportedPage(!reader, page)
                        document.NewPage() |> ignore
                        cb.AddTemplate(page, float32 0, float32 0)
                ) (head :: tail) |> ignore
                document.Close()
            Windows.MessageBox.Show("PDF변환완료") |> ignore

        if detailFileList.Items.Count > 0 then
            let detailFileArray =
                Array.init detailFileList.Items.Count (fun x -> ((detailFileList.Items.GetItemAt(x) :?> ListBoxItem).Content :?> String))
            let detailFileNameList = Array.toList(detailFileArray)
            let aux1 (fileName: String) =
                let file = new StreamReader(new FileStream(fileName, FileMode.Open), Text.Encoding.GetEncoding("euc-kr"))
                let rec aux2 item (pageContent: String) findFlag (resultList: (String * String) List) =
                    if file.EndOfStream then
                        resultList
                    else
                        let line = file.ReadLine()
                        if not (line.IndexOf("/NewPageSetup") = -1) then
                            aux2 "" (line + "\n") true resultList
                        elif not (line.IndexOf("showpage") = -1) then
                            if findFlag then
                                aux2 "" (line + "\n") findFlag ((item, pageContent + line + "\n") :: resultList)
                            else
                                aux2 "" "" findFlag resultList
                        elif not (line.IndexOf(detailPosition1) = -1) then
                            let item = line + item
                            aux2 item (pageContent + line + "\n") findFlag resultList
                        elif not (detailPosition2.Equals("")) && not (line.IndexOf(detailPosition2) = -1) then
                            let item =    item + line
                            aux2 item (pageContent + line + "\n") findFlag resultList
                        elif not (line.StartsWith("%")) then
                            if findFlag then
                                aux2 item (pageContent + line + "\n") findFlag resultList
                            else
                                aux2 item pageContent findFlag resultList
                        else
                            aux2 item pageContent findFlag resultList
                in
                let result = aux2 "" "" false []
                file.Close()
                result
            in
            let (filePrefix, firstPagePrefix) = 
                match detailFileNameList with
                | [] -> ("", "")
                | (head :: _)    ->
                    let file = new StreamReader(new FileStream(head, FileMode.Open), Text.Encoding.GetEncoding("euc-kr"))
                    let rec innerAux firstPageFindFlag ((filePrefix, firstPagePrefix) as result) =
                        if file.EndOfStream then
                            result
                        else
                            let line = file.ReadLine()
                            if line.StartsWith("/NewPageSetup") then
                                result
                            elif firstPageFindFlag then
                                innerAux firstPageFindFlag (filePrefix, firstPagePrefix + line + "\n")
                            elif line.StartsWith("%%Page:") then
                                innerAux true result
                            else
                                innerAux firstPageFindFlag (filePrefix + line + "\n", firstPagePrefix)
                    in
                    let result = innerAux false ("", "")
                    file.Close()
                    result
            in
            let sortedList = List.sortBy (fun (item, _) -> item) (List.concat (List.map aux1 detailFileNameList))
            let datFile = new StreamWriter(new FileStream(outputDatFile, FileMode.Create), Text.Encoding.GetEncoding("euc-kr"))
            datFile.Write(filePrefix)
            match sortedList with
            | [] -> ()
            | ((_, content) :: tail) ->
                datFile.Write("%%Page: 1 1\n")
                datFile.Write(firstPagePrefix)
                datFile.Write(content)
                let rec writeDatPage count pageList =
                    match pageList with
                    | [] -> ()
                    | ((_, content: String) :: innerTail) ->
                        datFile.Write("%%Page: " + count.ToString() + " " + count.ToString() + "\n")
                        datFile.Write(content)
                        writeDatPage (count + 1) innerTail
                in
                writeDatPage 2 tail
                datFile.Write("%COBRAEnd\n")
                datFile.Write("%Last CBR Format [COBRA2]\n")
                datFile.Write("%%Pages: " + (List.length sortedList).ToString() + "\n")
                datFile.Write("%%EOF")
            datFile.Close()
            Windows.MessageBox.Show("DAT변환완료") |> ignore
in

let save = (window.FindName("save") :?> Button)
save.Click.Add(convertFile)

[<STAThread>]
[<EntryPoint>]
let main args =
    app.Run window