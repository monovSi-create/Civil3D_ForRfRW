Option Explicit On
Option Strict Off

Imports DBTransactionManager = Autodesk.AutoCAD.DatabaseServices.TransactionManager
Imports Autodesk.AutoCAD.Internal
Imports Autodesk.Civil.DatabaseServices
Imports Autodesk.Civil.Runtime
Imports Autodesk.AutoCAD.DatabaseServices
Imports System.Math
Imports System.IO.Ports
Imports Autodesk.AutoCAD.Geometry
Public Class FoundationElement
    Inherits SATemplate

    ' *************************************************************************
    ' *************************************************************************
    ' *************************************************************************
    '          Name: BasicLane
    '
    '   Description: Creates a simple cross-sectional representation of foundation for facing elements.
    '
    ' Logical Names: Name                       Type       Optional  Description
    '                --------------------------------------------------------------
    '                TargetSurface              Surface    Yes       May be used to judge fill/cut condition
    '
    '
    ' Input Parameters: Name                   Type    Optional    Default Value    Description
    '                -------------------------------------------------------------------------------------------
    '                Сторона                   long        no          Right            specifies side to place SA on
    '                ШиринаФундамента          double      no          3.0              width of geogrids
    '                ВысотаФундамента          double      no          0.5              step of geogrid layer
    '                ОтступЗасыпки             double      no          1.5              0
    '                ТолщинаПодготовки         double      no           3               0
    '                ШагДоРешетки              double      no           1               0
    '                ПерехлестГеотекстиля      double      no          0.3              0
    '
    '
    'Output Parameters: Name               Type              Description
    '                ------------------------------------------------------------------
    '                None

    Private Const SideDefault = Utilities.Right
    Private Const WidthDefault = 0.8
    Private Const HeightDefault = 0.3
    Private Const dSoilOffset = 3
    Private Const dPrepHeight = 0.02
    Private Const dGeogridElevDefault = 0.25
    Private Const dGeotextileOverlapDefault = 0.3
    Private Const dFaceWidthDefault = 0.35
    Private Const dAssemblyNameDefault = "Участок"
    Private Const dInsertPoint = 1
    Private Const dWallInsert = 0.03
    Protected Overrides Sub GetLogicalNamesImplement(corridorState As CorridorState)
        MyBase.GetLogicalNamesImplement(corridorState)

        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong
        'add logical names we used to script
        Dim ParamLong As ParamLong
        ParamLong = paramsLong.Add("Граница засыпки", ParamLogicalNameType.OffsetTarget)
        ParamLong.DisplayName = "Граница засыпки"
    End Sub
    Protected Overrides Sub GetInputParametersImplement(ByVal corridorState As CorridorState)
        MyBase.GetInputParametersImplement(corridorState)

        ' define collection for long parameters in corridor
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        ' define collection for double parameters in corridor
        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble

        ' define collection for string parameters in corridor
        Dim paramsString As ParamStringCollection
        paramsString = corridorState.ParamsString

        ' Add input parameters we used in this script
        paramsLong.Add(Utilities.Side, SideDefault)
        paramsDouble.Add("Ширина Фундамента", WidthDefault)
        paramsDouble.Add("Высота Фундамента", HeightDefault)
        paramsDouble.Add("Отступ Засыпки", dSoilOffset)
        paramsDouble.Add("Толщина Подготовки", dPrepHeight)
        paramsDouble.Add("Шаг До Решетки", dGeogridElevDefault)
        paramsDouble.Add("Перехлест Геотекстиля", dGeotextileOverlapDefault)
        paramsDouble.Add("Толщина Облицовки", dFaceWidthDefault)
        paramsString.Add("Имя участка", dAssemblyNameDefault)
        paramsLong.Add("Точка вставки", dInsertPoint)
        paramsDouble.Add("Точка для присоединения армогрунта", dWallInsert)
    End Sub

    Protected Overrides Sub GetOutputParametersImplement(ByVal corridorState As CorridorState)
        MyBase.GetOutputParametersImplement(corridorState)

        ' Retrieve parameter buckets from the corridor state
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble

        Dim paramsString As ParamStringCollection
        paramsString = corridorState.ParamsString


        ' Add the output parameters we use in this script
        Dim param As IParam

        param = paramsLong.Add(Utilities.Side, SideDefault)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsDouble.Add("Ширина Фундамента", WidthDefault)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsDouble.Add("Высота Фундамента", HeightDefault)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsDouble.Add("Отступ Засыпки", dSoilOffset)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsDouble.Add("Толщина Подготовки", dPrepHeight)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsDouble.Add("Шаг До Решетки", dGeogridElevDefault)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsDouble.Add("Перехлест Геотекстиля", dGeotextileOverlapDefault)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsDouble.Add("Толщина Облицовки", dFaceWidthDefault)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsString.Add("Имя участка", dAssemblyNameDefault)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput
    End Sub

    Protected Overrides Sub DrawImplement(ByVal corridorState As CorridorState)

        Dim tm As DBTransactionManager
        tm = Autodesk.AutoCAD.DatabaseServices.HostApplicationServices.WorkingDatabase.TransactionManager

        'Dim oParamsSurface As ParamSurfaceCollection
        'oParamsSurface = corridorState.ParamsSurface

        Dim oParamsOffsetTarget As ParamOffsetTargetCollection
        oParamsOffsetTarget = corridorState.ParamsOffsetTarget

        'Dim oIntersectionPointWithSurface As IPoint = Nothing

        ' Retrieve parameter buckets from the corridor state
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble

        Dim paramsString As ParamStringCollection
        paramsString = corridorState.ParamsString

        '-----------------------------------------
        ' on error resume next

        Dim side As Long
        Try
            side = paramsLong.Value(Utilities.Side)
        Catch
            side = SideDefault
        End Try

        '----------------------------------------
        'flip about Y axis

        Dim flip As Double
        flip = 1.0#
        If side = Utilities.Left Then
            flip = -1.0#
        End If
        '----------------------------------------
        'foundation dimensions
        Dim width As Double
        Try
            width = paramsDouble.Value("Ширина Фундамента")
        Catch
            width = WidthDefault
        End Try
        '-----------------------------------------
        Dim height As Double
        Try
            height = paramsDouble.Value("Высота Фундамента")
        Catch
            height = HeightDefault
        End Try
        '----------------------------------------
        Dim soilOffset As Double
        Try
            soilOffset = paramsDouble.Value("Отступ Засыпки")
        Catch
            soilOffset = dSoilOffset
        End Try
        '----------------------------------------
        Dim prepHeight As Double
        Try
            prepHeight = paramsDouble.Value("Толщина Подготовки")
        Catch
            prepHeight = dPrepHeight
        End Try
        '----------------------------------------
        Dim geogridElev As Double
        Try
            geogridElev = paramsDouble.Value("Шаг До Решетки")
        Catch
            geogridElev = dGeogridElevDefault
        End Try
        '----------------------------------------
        Dim geotextileOverlap As Double
        Try
            geotextileOverlap = paramsDouble.Value("Перехлест Геотекстиля")
        Catch
            geotextileOverlap = dGeotextileOverlapDefault
        End Try
        '----------------------------------------
        Dim faceWidth As Double
        Try
            faceWidth = paramsDouble.Value("Толщина Облицовки")
        Catch
            faceWidth = dFaceWidthDefault
        End Try
        '----------------------------------------
        Dim oAssemblyName As String
        Try
            oAssemblyName = paramsString.Value("Имя участка")
        Catch
            oAssemblyName = dAssemblyNameDefault
        End Try
        '----------------------------------------
        Dim insertPointN As Long
        Try
            insertPointN = paramsLong.Value("Точка вставки")
        Catch
            insertPointN = dInsertPoint
        End Try
        '----------------------------------------
        Dim wallInsert As Double
        Try
            wallInsert = paramsDouble.Value("Точка для присоединения армогрунта")
        Catch
            wallInsert = dWallInsert
        End Try
        '---------------------------------------------------------

        '--------------------------------------------------------
        Dim concLeveling As String = "Цементная подготовка"
        Dim foundationConcrete As String = "Фундамент"
        Dim soil As String = "Дренирующий грунт"
        Dim geotextile As String = "geotextile"
        Dim gidro As String = "gidroizolUp"

        Dim oOrigin As New PointInMem
        Dim oCurrentAlignmentId As ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, oCurrentAlignmentId, oOrigin)

        If corridorState.Mode <> CorridorMode.Layout Then
            Dim offsetTarget As WidthOffsetTarget
            Try
                offsetTarget = oParamsOffsetTarget.Value("Граница засыпки")
            Catch
                offsetTarget = Nothing
            End Try
            Dim hasWallOffsetTarget As Boolean
            hasWallOffsetTarget = False

            Dim xOffset As Double
            Dim yOffset As Double
            Dim soilOffsetT As Double

            If Not offsetTarget Is Nothing Then
                Try
                    Utilities.CalcAlignmentOffsetToThisAlignment(oCurrentAlignmentId, corridorState.CurrentStation, offsetTarget, soilOffsetT, xOffset, yOffset)
                    hasWallOffsetTarget = True
                    soilOffsetT = soilOffsetT - oOrigin.Offset
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "Граница засыпки", "RetainWallHorizontal")
                End Try
            Else
                soilOffsetT = soilOffset * flip
            End If
            'создаем поперечные сечения
            If insertPointN = 2 Then
                FoundationToUpper(corridorState,
                                         flip,
                                         width,
                                         height,
                                         faceWidth,
                                         prepHeight,
                                         soilOffsetT,
                                         geotextileOverlap,
                                         geogridElev,
                                         oAssemblyName,
                                         foundationConcrete,
                                         concLeveling,
                                         gidro,
                                         soil,
                                         geotextile,
                                         wallInsert)
            ElseIf insertPointN = 3 Then
                FoundationToPrep(corridorState,
                                         flip,
                                         width,
                                         height,
                                         faceWidth,
                                         prepHeight,
                                         soilOffsetT,
                                         geotextileOverlap,
                                         geogridElev,
                                         oAssemblyName,
                                         foundationConcrete,
                                         concLeveling,
                                         gidro,
                                         soil,
                                         geotextile,
                                         wallInsert)
            Else
                FoundationToLower(corridorState,
                                         flip,
                                         width,
                                         height,
                                         faceWidth,
                                         prepHeight,
                                         soilOffsetT,
                                         geotextileOverlap,
                                         geogridElev,
                                         oAssemblyName,
                                         foundationConcrete,
                                         concLeveling,
                                         gidro,
                                         soil,
                                         geotextile,
                                         wallInsert)
            End If
        Else 'for layout mode
            Dim soilOffsetT As Double
            soilOffsetT = soilOffset * flip
            If insertPointN = 2 Then
                FoundationToUpper(corridorState,
                                         flip,
                                         width,
                                         height,
                                         faceWidth,
                                         prepHeight,
                                         soilOffsetT,
                                         geotextileOverlap,
                                         geogridElev,
                                         oAssemblyName,
                                         foundationConcrete,
                                         concLeveling,
                                         gidro,
                                         soil,
                                         geotextile,
                                         wallInsert)
            ElseIf insertPointN = 3 Then
                FoundationToPrep(corridorState,
                                         flip,
                                         width,
                                         height,
                                         faceWidth,
                                         prepHeight,
                                         soilOffsetT,
                                         geotextileOverlap,
                                         geogridElev,
                                         oAssemblyName,
                                         foundationConcrete,
                                         concLeveling,
                                         gidro,
                                         soil,
                                         geotextile,
                                         wallInsert)
            Else
                FoundationToLower(corridorState,
                                         flip,
                                         width,
                                         height,
                                         faceWidth,
                                         prepHeight,
                                         soilOffsetT,
                                         geotextileOverlap,
                                         geogridElev,
                                         oAssemblyName,
                                         foundationConcrete,
                                         concLeveling,
                                         gidro,
                                         soil,
                                         geotextile,
                                         wallInsert)
            End If


        End If
        '------------------------------------------------------
        Dim param As IParam

        param = paramsLong.Add(Utilities.Side, SideDefault)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsDouble.Add("Ширина Фундамента", WidthDefault)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsDouble.Add("Высота Фундамента", HeightDefault)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsDouble.Add("Отступ Засыпки", dSoilOffset)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsDouble.Add("Толщина Подготовки", dPrepHeight)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsDouble.Add("Шаг До Решетки", dGeogridElevDefault)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsDouble.Add("Перехлест Геотекстиля", dGeotextileOverlapDefault)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsDouble.Add("Толщина Облицовки", dFaceWidthDefault)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsString.Add("Имя участка", dAssemblyNameDefault)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsLong.Add("Точка вставки", dInsertPoint)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsDouble.Add("Точка для присоединения армогрунта", dWallInsert)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput
    End Sub
    Public Sub FoundationToLower(corridorState As CorridorState,
                                 flip As Double,
                                 width As Double,
                                 height As Double,
                                 faceWidth As Double,
                                 prepHeight As Double,
                                 soilOffset As Double,
                                 geotextileOverlap As Double,
                                 geogridElev As Double,
                                 oAssemblyName As String,
                                 foundationConcrete As String,
                                 concLeveling As String,
                                 gidro As String,
                                 soil As String,
                                 geotextile As String,
                                 helpPointOffset As Double)
        '---------------------------------------------------------
        ' Create points
        '---------------------------------------------------------
        'объявляем коллекции точек, связей и форм
        Dim foundPoints As PointCollection
        foundPoints = corridorState.Points
        Dim foundLinks As LinkCollection
        foundLinks = corridorState.Links
        Dim Shapes As ShapeCollection
        Shapes = corridorState.Shapes
        '------------------------------------
        Dim preparePoints As PointCollection
        preparePoints = corridorState.Points
        Dim prepareLinks As LinkCollection
        prepareLinks = corridorState.Links
        '------------------------------------
        Dim sandPoints As PointCollection
        sandPoints = corridorState.Points
        Dim sandLinks As LinkCollection
        sandLinks = corridorState.Links
        '------------------------------------
        Dim geotxtPoints As PointCollection
        geotxtPoints = corridorState.Points
        Dim geotxtLinks As LinkCollection
        geotxtLinks = corridorState.Links
        '------------------------------------
        Dim gidroPoints As PointCollection
        gidroPoints = corridorState.Points
        Dim gidroLinks As LinkCollection
        gidroLinks = corridorState.Links
        '------------------------------------
        Dim foundatP1 As Point
        Dim foundatP2 As Point
        Dim foundatP3 As Point
        Dim foundatP4 As Point
        Dim foundatP5 As Point
        Dim foundatP6 As Point
        Dim foundatP7 As Point
        Dim foundatP8 As Point
        Dim sandP9 As Point
        Dim sandP10 As Point
        Dim sandP11 As Point
        Dim sandP12 As Point
        Dim geotxtP11 As Point
        Dim geotxtP12 As Point
        Dim geotxtP13 As Point
        Dim geotxtP14 As Point
        Dim geotxtP15 As Point
        Dim geotxtP16 As Point
        Dim gidroP1 As Point
        Dim gidroP2 As Point
        Dim gidroP3 As Point
        Dim gidroP4 As Point
        Dim gidroP5 As Point
        Dim gidroP6 As Point

        Dim helpPoint As Point

        Dim foundatLink1 As Link
        Dim foundatLink2 As Link
        Dim foundatLink3 As Link
        Dim foundatLink4 As Link
        Dim foundatLink5 As Link
        Dim foundatLink6 As Link
        Dim foundatLink7 As Link
        Dim foundatLink8 As Link
        Dim gidroL1 As Link
        Dim gidroL2 As Link
        Dim gidroL3 As Link
        Dim gidroL4 As Link
        Dim gidroL5 As Link

        Dim sandLink10 As Link
        Dim sandLink11 As Link
        Dim sandLink12 As Link
        Dim sandLink13 As Link
        Dim geotxtLink13 As Link
        Dim geotxtLink14 As Link
        Dim geotxtLink15 As Link
        Dim geotxtLink16 As Link
        Dim geotxtLink17 As Link

        Dim foundShape As Autodesk.Civil.DatabaseServices.Shape
        Dim prepareShape As Autodesk.Civil.DatabaseServices.Shape
        Dim sandShape As Autodesk.Civil.DatabaseServices.Shape
        '--------------------------------------------------------
        'создаем фундамент
        foundatP1 = foundPoints.Add(0, 0, "Ось фундамента")
        foundatP2 = foundPoints.Add(foundatP1.Offset + flip * width / 2, foundatP1.Elevation, "")
        foundatP3 = foundPoints.Add(foundatP2.Offset, foundatP2.Elevation + height, "")
        foundatP4 = foundPoints.Add(foundatP3.Offset - width * flip, foundatP3.Elevation, "")
        foundatP5 = foundPoints.Add(foundatP4.Offset, foundatP4.Elevation - height, "")

        helpPoint = foundPoints.Add(foundatP3.Offset - helpPointOffset * flip, foundatP3.Elevation, "")

        foundatLink1 = foundLinks.Add(foundatP2, foundatP3, "")
        foundatLink2 = foundLinks.Add(foundatP3, foundatP4, "")
        foundatLink3 = foundLinks.Add(foundatP4, foundatP5, "")
        foundatLink4 = foundLinks.Add(foundatP5, foundatP2, "")

        foundShape = Shapes.Add(foundLinks.ToArray, oAssemblyName & "_" & foundationConcrete)

        'создаем подготовку под блоки
        foundatP6 = foundPoints.Add(foundatP1.Offset, foundatP1.Elevation + height + prepHeight, "Ось первого ряда блоков")
        foundatP7 = preparePoints.Add(foundatP6.Offset + flip * faceWidth / 2, foundatP6.Elevation, "")
        foundatP8 = preparePoints.Add(foundatP6.Offset - flip * faceWidth / 2, foundatP6.Elevation, "")

        foundatLink5 = prepareLinks.Add(foundatP3, foundatP7, "")
        foundatLink6 = prepareLinks.Add(foundatP7, foundatP8, "")
        foundatLink7 = prepareLinks.Add(foundatP8, foundatP4, "")

        foundatLink8 = prepareLinks.Add(foundatP4, foundatP3, "")

        prepareShape = Shapes.Add(foundatLink5, foundatLink6, foundatLink7, foundatLink8, oAssemblyName & "_" & concLeveling)

        'создаем гидроизоляцию
        gidroP1 = gidroPoints.Add(foundatP5.Offset - 0.001, foundatP5.Elevation, oAssemblyName & "_" & "0" & "_" & gidro & 1)
        gidroP2 = gidroPoints.Add(foundatP4.Offset, foundatP4.Elevation, "")
        gidroP3 = gidroPoints.Add(foundatP8.Offset, foundatP8.Elevation, "")
        gidroP4 = gidroPoints.Add(foundatP7.Offset, foundatP7.Elevation, "")
        gidroP5 = gidroPoints.Add(foundatP3.Offset, foundatP3.Elevation, "")
        gidroP6 = gidroPoints.Add(foundatP2.Offset + 0.001, foundatP2.Elevation, oAssemblyName & "_" & "0" & "_" & gidro & 2)

        gidroL1 = gidroLinks.Add(gidroP1, gidroP2, oAssemblyName & "_" & "0" & "_" & gidro)
        gidroL2 = gidroLinks.Add(gidroP2, gidroP3, oAssemblyName & "_" & "0" & "_" & gidro)
        gidroL3 = gidroLinks.Add(gidroP3, gidroP4, oAssemblyName & "_" & "0" & "_" & gidro)
        gidroL4 = gidroLinks.Add(gidroP4, gidroP5, oAssemblyName & "_" & "0" & "_" & gidro)
        gidroL5 = gidroLinks.Add(gidroP5, gidroP6, oAssemblyName & "_" & "0" & "_" & gidro)

        'создаем песок
        sandP9 = sandPoints.Add(foundatP2.Offset, foundatP2.Elevation, oAssemblyName & "_" & "00" & "_" & "sand" & 1)
        sandP10 = sandPoints.Add(foundatP3.Offset, foundatP3.Elevation, oAssemblyName & "_" & "00" & "_" & "sand" & 2)
        sandP11 = sandPoints.Add(soilOffset, sandP10.Elevation, oAssemblyName & "_" & "00" & "_" & "sand" & 3)
        sandP12 = sandPoints.Add(soilOffset, sandP9.Elevation, oAssemblyName & "_" & "00" & "_" & "sand" & 4)


        sandLink10 = sandLinks.Add(sandP9, sandP10, oAssemblyName & "_" & "00" & "_" & "sandUp")
        sandLink11 = sandLinks.Add(sandP10, sandP11, oAssemblyName & "_" & "00" & "_" & "sandUp")
        sandLink12 = sandLinks.Add(sandP9, sandP12, oAssemblyName & "_" & "00" & "_" & "sandDown")
        sandLink13 = sandLinks.Add(sandP12, sandP11, oAssemblyName & "_" & "00" & "_" & "sandDown")

        sandShape = Shapes.Add(sandLink10, sandLink11, sandLink13, sandLink12, oAssemblyName & "_" & soil)

        'создаем геотекстиль
        geotxtP11 = geotxtPoints.Add(sandP12.Offset, sandP12.Elevation, oAssemblyName & "_" & "0" & "_" & geotextile & 1)
        geotxtP12 = geotxtPoints.Add(sandP9.Offset, sandP9.Elevation, oAssemblyName & "_" & "0" & "_" & geotextile)
        geotxtP13 = geotxtPoints.Add(foundatP3.Offset, foundatP3.Elevation, oAssemblyName & "_" & "0" & "_" & geotextile)
        geotxtP14 = geotxtPoints.Add(foundatP7.Offset, foundatP7.Elevation, oAssemblyName & "_" & "0" & "_" & geotextile & 2)
        geotxtP15 = geotxtPoints.Add(foundatP7.Offset, foundatP7.Elevation + geogridElev, oAssemblyName & "_" & "0" & "_" & geotextile)
        geotxtP16 = geotxtPoints.Add(geotxtP15.Offset + flip * geotextileOverlap, geotxtP15.Elevation, oAssemblyName & "_" & "0" & "_" & geotextile & 4)

        geotxtLink13 = geotxtLinks.Add(geotxtP11, geotxtP12, oAssemblyName & "_" & "0" & "_" & geotextile & "Down")
        geotxtLink14 = geotxtLinks.Add(geotxtP12, geotxtP13, oAssemblyName & "_" & "0" & "_" & geotextile & "Down")
        geotxtLink15 = geotxtLinks.Add(geotxtP13, geotxtP14, oAssemblyName & "_" & "0" & "_" & geotextile & "Down")
        geotxtLink16 = geotxtLinks.Add(geotxtP14, geotxtP15, oAssemblyName & "_" & "0" & "_" & geotextile & "Up")
        geotxtLink17 = geotxtLinks.Add(geotxtP15, geotxtP16, oAssemblyName & "_" & "0" & "_" & geotextile & "Up")

    End Sub
    Public Sub FoundationToUpper(corridorState As CorridorState,
                                 flip As Double,
                                 width As Double,
                                 height As Double,
                                 faceWidth As Double,
                                 prepHeight As Double,
                                 soilOffset As Double,
                                 geotextileOverlap As Double,
                                 geogridElev As Double,
                                 oAssemblyName As String,
                                 foundationConcrete As String,
                                 concLeveling As String,
                                 gidro As String,
                                 soil As String,
                                 geotextile As String,
                                 helpPointOffset As Double)
        '---------------------------------------------------------
        ' Create points
        '---------------------------------------------------------
        'объявляем коллекции точек, связей и форм
        Dim foundPoints As PointCollection
        foundPoints = corridorState.Points
        Dim foundLinks As LinkCollection
        foundLinks = corridorState.Links
        Dim Shapes As ShapeCollection
        Shapes = corridorState.Shapes
        '------------------------------------
        Dim preparePoints As PointCollection
        preparePoints = corridorState.Points
        Dim prepareLinks As LinkCollection
        prepareLinks = corridorState.Links
        '------------------------------------
        Dim sandPoints As PointCollection
        sandPoints = corridorState.Points
        Dim sandLinks As LinkCollection
        sandLinks = corridorState.Links
        '------------------------------------
        Dim geotxtPoints As PointCollection
        geotxtPoints = corridorState.Points
        Dim geotxtLinks As LinkCollection
        geotxtLinks = corridorState.Links
        '------------------------------------
        Dim gidroPoints As PointCollection
        gidroPoints = corridorState.Points
        Dim gidroLinks As LinkCollection
        gidroLinks = corridorState.Links
        '------------------------------------
        Dim foundatP1 As Point
        Dim foundatP2 As Point
        Dim foundatP3 As Point
        Dim foundatP4 As Point
        Dim foundatP5 As Point
        Dim foundatP6 As Point
        Dim foundatP7 As Point
        Dim foundatP8 As Point
        Dim foundatP9 As Point
        Dim sandP9 As Point
        Dim sandP10 As Point
        Dim sandP11 As Point
        Dim sandP12 As Point
        Dim geotxtP11 As Point
        Dim geotxtP12 As Point
        Dim geotxtP13 As Point
        Dim geotxtP14 As Point
        Dim geotxtP15 As Point
        Dim geotxtP16 As Point
        Dim gidroP1 As Point
        Dim gidroP2 As Point
        Dim gidroP3 As Point
        Dim gidroP4 As Point
        Dim gidroP5 As Point
        Dim gidroP6 As Point

        Dim helpPoint As Point

        Dim foundatLink1 As Link
        Dim foundatLink2 As Link
        Dim foundatLink3 As Link
        Dim foundatLink4 As Link
        Dim foundatLink5 As Link
        Dim foundatLink6 As Link
        Dim foundatLink7 As Link
        Dim foundatLink8 As Link
        Dim gidroL1 As Link
        Dim gidroL2 As Link
        Dim gidroL3 As Link
        Dim gidroL4 As Link
        Dim gidroL5 As Link

        Dim sandLink10 As Link
        Dim sandLink11 As Link
        Dim sandLink12 As Link
        Dim sandLink13 As Link
        Dim geotxtLink13 As Link
        Dim geotxtLink14 As Link
        Dim geotxtLink15 As Link
        Dim geotxtLink16 As Link
        Dim geotxtLink17 As Link

        Dim foundShape As Autodesk.Civil.DatabaseServices.Shape
        Dim prepareShape As Autodesk.Civil.DatabaseServices.Shape
        Dim sandShape As Autodesk.Civil.DatabaseServices.Shape
        '--------------------------------------------------------
        'создаем фундамент
        foundatP1 = foundPoints.Add(0, 0, "")
        foundatP2 = foundPoints.Add(foundatP1.Offset + flip * width / 2, foundatP1.Elevation, "")
        foundatP3 = foundPoints.Add(foundatP2.Offset, foundatP2.Elevation - height, "")
        foundatP4 = foundPoints.Add(foundatP3.Offset - width * flip, foundatP3.Elevation, "")
        foundatP5 = foundPoints.Add(foundatP4.Offset, foundatP4.Elevation + height, "")
        foundatP9 = foundPoints.Add(foundatP1.Offset, foundatP1.Elevation - height, "Ось фундамента")

        helpPoint = foundPoints.Add(foundatP2.Offset - helpPointOffset * flip, foundatP2.Elevation, "")

        foundatLink1 = foundLinks.Add(foundatP2, foundatP3, "")
        foundatLink2 = foundLinks.Add(foundatP3, foundatP4, "")
        foundatLink3 = foundLinks.Add(foundatP4, foundatP5, "")
        foundatLink4 = foundLinks.Add(foundatP5, foundatP2, "")

        foundShape = Shapes.Add(foundLinks.ToArray, oAssemblyName & "_" & foundationConcrete)

        'создаем подготовку под блоки
        foundatP6 = foundPoints.Add(foundatP1.Offset, foundatP1.Elevation + prepHeight, "Ось первого ряда блоков")
        foundatP7 = preparePoints.Add(foundatP6.Offset + flip * faceWidth / 2, foundatP6.Elevation, "")
        foundatP8 = preparePoints.Add(foundatP6.Offset - flip * faceWidth / 2, foundatP6.Elevation, "")

        foundatLink5 = prepareLinks.Add(foundatP2, foundatP7, "")
        foundatLink6 = prepareLinks.Add(foundatP7, foundatP8, "")
        foundatLink7 = prepareLinks.Add(foundatP8, foundatP5, "")

        foundatLink8 = prepareLinks.Add(foundatP2, foundatP5, "")

        prepareShape = Shapes.Add(foundatLink5, foundatLink6, foundatLink7, foundatLink8, oAssemblyName & "_" & concLeveling)
        'создаем гидроизоляцию
        gidroP1 = gidroPoints.Add(foundatP4.Offset - 0.001, foundatP4.Elevation, oAssemblyName & "_" & "0" & "_" & gidro & 1)
        gidroP2 = gidroPoints.Add(foundatP5.Offset, foundatP5.Elevation, "")
        gidroP3 = gidroPoints.Add(foundatP8.Offset, foundatP8.Elevation, "")
        gidroP4 = gidroPoints.Add(foundatP7.Offset, foundatP7.Elevation, "")
        gidroP5 = gidroPoints.Add(foundatP2.Offset, foundatP2.Elevation, "")
        gidroP6 = gidroPoints.Add(foundatP3.Offset + 0.001, foundatP3.Elevation, oAssemblyName & "_" & "0" & "_" & gidro & 2)

        gidroL1 = gidroLinks.Add(gidroP1, gidroP2, oAssemblyName & "_" & "0" & "_" & gidro)
        gidroL2 = gidroLinks.Add(gidroP2, gidroP3, oAssemblyName & "_" & "0" & "_" & gidro)
        gidroL3 = gidroLinks.Add(gidroP3, gidroP4, oAssemblyName & "_" & "0" & "_" & gidro)
        gidroL4 = gidroLinks.Add(gidroP4, gidroP5, oAssemblyName & "_" & "0" & "_" & gidro)
        gidroL5 = gidroLinks.Add(gidroP5, gidroP6, oAssemblyName & "_" & "0" & "_" & gidro)


        'создаем песок
        sandP9 = sandPoints.Add(foundatP3.Offset, foundatP3.Elevation, oAssemblyName & "_" & "00" & "_" & "sand" & 1)
        sandP10 = sandPoints.Add(foundatP2.Offset, foundatP2.Elevation, oAssemblyName & "_" & "00" & "_" & "sand" & 2)
        sandP11 = sandPoints.Add(soilOffset, sandP10.Elevation, oAssemblyName & "_" & "00" & "_" & "sand" & 3)
        sandP12 = sandPoints.Add(soilOffset, sandP9.Elevation, oAssemblyName & "_" & "00" & "_" & "sand" & 4)


        sandLink10 = sandLinks.Add(sandP9, sandP10, oAssemblyName & "_" & "00" & "_" & "sandUp")
        sandLink11 = sandLinks.Add(sandP10, sandP11, oAssemblyName & "_" & "00" & "_" & "sandUp")
        sandLink12 = sandLinks.Add(sandP9, sandP12, oAssemblyName & "_" & "00" & "_" & "sandDown")
        sandLink13 = sandLinks.Add(sandP12, sandP11, oAssemblyName & "_" & "00" & "_" & "sandDown")

        sandShape = Shapes.Add(sandLink10, sandLink11, sandLink13, sandLink12, oAssemblyName & "_" & soil)
        'создаем геотекстиль
        geotxtP11 = geotxtPoints.Add(sandP12.Offset, sandP12.Elevation, oAssemblyName & "_" & "0" & "_" & geotextile & 1)
        geotxtP12 = geotxtPoints.Add(sandP9.Offset, sandP9.Elevation, oAssemblyName & "_" & "0" & "_" & geotextile)
        geotxtP13 = geotxtPoints.Add(foundatP2.Offset, foundatP2.Elevation, oAssemblyName & "_" & "0" & "_" & geotextile)
        geotxtP14 = geotxtPoints.Add(foundatP7.Offset, foundatP7.Elevation, oAssemblyName & "_" & "0" & "_" & geotextile & 2)
        geotxtP15 = geotxtPoints.Add(foundatP7.Offset, foundatP7.Elevation + geogridElev, oAssemblyName & "_" & "0" & "_" & geotextile)
        geotxtP16 = geotxtPoints.Add(geotxtP15.Offset + flip * geotextileOverlap, geotxtP15.Elevation, oAssemblyName & "_" & "0" & "_" & geotextile & 4)

        geotxtLink13 = geotxtLinks.Add(geotxtP11, geotxtP12, oAssemblyName & "_" & "0" & "_" & geotextile & "Down")
        geotxtLink14 = geotxtLinks.Add(geotxtP12, geotxtP13, oAssemblyName & "_" & "0" & "_" & geotextile & "Down")
        geotxtLink15 = geotxtLinks.Add(geotxtP13, geotxtP14, oAssemblyName & "_" & "0" & "_" & geotextile & "Down")
        geotxtLink16 = geotxtLinks.Add(geotxtP14, geotxtP15, oAssemblyName & "_" & "0" & "_" & geotextile & "Up")
        geotxtLink17 = geotxtLinks.Add(geotxtP15, geotxtP16, oAssemblyName & "_" & "0" & "_" & geotextile & "Up")
    End Sub
    Public Sub FoundationToPrep(corridorState As CorridorState,
                                 flip As Double,
                                 width As Double,
                                 height As Double,
                                 faceWidth As Double,
                                 prepHeight As Double,
                                 soilOffset As Double,
                                 geotextileOverlap As Double,
                                 geogridElev As Double,
                                 oAssemblyName As String,
                                 foundationConcrete As String,
                                 concLeveling As String,
                                 gidro As String,
                                 soil As String,
                                 geotextile As String,
                                 helpPointOffset As Double)
        '---------------------------------------------------------
        ' Create points
        '---------------------------------------------------------
        'объявляем коллекции точек, связей и форм
        Dim foundPoints As PointCollection
        foundPoints = corridorState.Points
        Dim foundLinks As LinkCollection
        foundLinks = corridorState.Links
        Dim Shapes As ShapeCollection
        Shapes = corridorState.Shapes
        '------------------------------------
        Dim preparePoints As PointCollection
        preparePoints = corridorState.Points
        Dim prepareLinks As LinkCollection
        prepareLinks = corridorState.Links
        '------------------------------------
        Dim sandPoints As PointCollection
        sandPoints = corridorState.Points
        Dim sandLinks As LinkCollection
        sandLinks = corridorState.Links
        '------------------------------------
        Dim geotxtPoints As PointCollection
        geotxtPoints = corridorState.Points
        Dim geotxtLinks As LinkCollection
        geotxtLinks = corridorState.Links
        '------------------------------------
        Dim gidroPoints As PointCollection
        gidroPoints = corridorState.Points
        Dim gidroLinks As LinkCollection
        gidroLinks = corridorState.Links
        '------------------------------------
        Dim foundatP1 As Point
        Dim foundatP2 As Point
        Dim foundatP3 As Point
        Dim foundatP4 As Point
        Dim foundatP5 As Point
        Dim foundatP6 As Point
        Dim foundatP7 As Point
        Dim foundatP8 As Point

        Dim sandP9 As Point
        Dim sandP10 As Point
        Dim sandP11 As Point
        Dim sandP12 As Point
        Dim geotxtP11 As Point
        Dim geotxtP12 As Point
        Dim geotxtP13 As Point
        Dim geotxtP14 As Point
        Dim geotxtP15 As Point
        Dim geotxtP16 As Point
        Dim gidroP1 As Point
        Dim gidroP2 As Point
        Dim gidroP3 As Point
        Dim gidroP4 As Point
        Dim gidroP5 As Point
        Dim gidroP6 As Point

        Dim helpPoint As Point

        Dim foundatLink1 As Link
        Dim foundatLink2 As Link
        Dim foundatLink3 As Link
        Dim foundatLink4 As Link
        Dim foundatLink5 As Link
        Dim foundatLink6 As Link
        Dim foundatLink7 As Link
        Dim foundatLink8 As Link
        Dim gidroL1 As Link
        Dim gidroL2 As Link
        Dim gidroL3 As Link
        Dim gidroL4 As Link
        Dim gidroL5 As Link

        Dim sandLink10 As Link
        Dim sandLink11 As Link
        Dim sandLink12 As Link
        Dim sandLink13 As Link
        Dim geotxtLink13 As Link
        Dim geotxtLink14 As Link
        Dim geotxtLink15 As Link
        Dim geotxtLink16 As Link
        Dim geotxtLink17 As Link

        Dim foundShape As Autodesk.Civil.DatabaseServices.Shape
        Dim prepareShape As Autodesk.Civil.DatabaseServices.Shape
        Dim sandShape As Autodesk.Civil.DatabaseServices.Shape
        '--------------------------------------------------------
        'создаем фундамент
        foundatP1 = foundPoints.Add(0, 0, "Ось первого ряда блоков")
        foundatP2 = foundPoints.Add(foundatP1.Offset + flip * width / 2, foundatP1.Elevation - prepHeight, "")
        foundatP3 = foundPoints.Add(foundatP2.Offset, foundatP2.Elevation - height, "")
        foundatP4 = foundPoints.Add(foundatP3.Offset - width * flip, foundatP3.Elevation, "")
        foundatP5 = foundPoints.Add(foundatP4.Offset, foundatP4.Elevation + height, "")
        foundatP6 = foundPoints.Add(foundatP1.Offset, foundatP1.Elevation - height - prepHeight, "Ось фундамента")

        helpPoint = foundPoints.Add(foundatP2.Offset - helpPointOffset * flip, foundatP2.Elevation, "")

        foundatLink1 = foundLinks.Add(foundatP2, foundatP3, "")
        foundatLink2 = foundLinks.Add(foundatP3, foundatP4, "")
        foundatLink3 = foundLinks.Add(foundatP4, foundatP5, "")
        foundatLink4 = foundLinks.Add(foundatP5, foundatP2, "")

        foundShape = Shapes.Add(foundLinks.ToArray, oAssemblyName & "_" & foundationConcrete)

        'создаем подготовку под блоки
        foundatP7 = preparePoints.Add(foundatP1.Offset + flip * faceWidth / 2, foundatP1.Elevation, "")
        foundatP8 = preparePoints.Add(foundatP1.Offset - flip * faceWidth / 2, foundatP1.Elevation, "")

        foundatLink5 = prepareLinks.Add(foundatP2, foundatP7, "")
        foundatLink6 = prepareLinks.Add(foundatP7, foundatP8, "")
        foundatLink7 = prepareLinks.Add(foundatP8, foundatP5, "")

        foundatLink8 = prepareLinks.Add(foundatP5, foundatP2, "")

        prepareShape = Shapes.Add(foundatLink5, foundatLink6, foundatLink7, foundatLink8, oAssemblyName & "_" & concLeveling)
        'создаем гидроизоляцию
        gidroP1 = gidroPoints.Add(foundatP4.Offset - 0.001, foundatP4.Elevation, oAssemblyName & "_" & "0" & "_" & gidro & 1)
        gidroP2 = gidroPoints.Add(foundatP5.Offset, foundatP5.Elevation, "")
        gidroP3 = gidroPoints.Add(foundatP8.Offset, foundatP8.Elevation, "")
        gidroP4 = gidroPoints.Add(foundatP7.Offset, foundatP7.Elevation, "")
        gidroP5 = gidroPoints.Add(foundatP2.Offset, foundatP2.Elevation, "")
        gidroP6 = gidroPoints.Add(foundatP3.Offset + 0.001, foundatP3.Elevation, oAssemblyName & "_" & "0" & "_" & gidro & 2)

        gidroL1 = gidroLinks.Add(gidroP1, gidroP2, oAssemblyName & "_" & "0" & "_" & gidro)
        gidroL2 = gidroLinks.Add(gidroP2, gidroP3, oAssemblyName & "_" & "0" & "_" & gidro)
        gidroL3 = gidroLinks.Add(gidroP3, gidroP4, oAssemblyName & "_" & "0" & "_" & gidro)
        gidroL4 = gidroLinks.Add(gidroP4, gidroP5, oAssemblyName & "_" & "0" & "_" & gidro)
        gidroL5 = gidroLinks.Add(gidroP5, gidroP6, oAssemblyName & "_" & "0" & "_" & gidro)


        'создаем песок
        sandP9 = sandPoints.Add(foundatP3.Offset, foundatP3.Elevation, oAssemblyName & "_" & "00" & "_" & "sand" & 1)
        sandP10 = sandPoints.Add(foundatP2.Offset, foundatP2.Elevation, oAssemblyName & "_" & "00" & "_" & "sand" & 2)
        sandP11 = sandPoints.Add(soilOffset, sandP10.Elevation, oAssemblyName & "_" & "00" & "_" & "sand" & 3)
        sandP12 = sandPoints.Add(soilOffset, sandP9.Elevation, oAssemblyName & "_" & "00" & "_" & "sand" & 4)


        sandLink10 = sandLinks.Add(sandP9, sandP10, oAssemblyName & "_" & "00" & "_" & "sandUp")
        sandLink11 = sandLinks.Add(sandP10, sandP11, oAssemblyName & "_" & "00" & "_" & "sandUp")
        sandLink12 = sandLinks.Add(sandP9, sandP12, oAssemblyName & "_" & "00" & "_" & "sandDown")
        sandLink13 = sandLinks.Add(sandP12, sandP11, oAssemblyName & "_" & "00" & "_" & "sandDown")

        sandShape = Shapes.Add(sandLink10, sandLink11, sandLink13, sandLink12, oAssemblyName & "_" & soil)
        'создаем геотекстиль
        geotxtP11 = geotxtPoints.Add(sandP12.Offset, sandP12.Elevation, oAssemblyName & "_" & "0" & "_" & geotextile & 1)
        geotxtP12 = geotxtPoints.Add(sandP9.Offset, sandP9.Elevation, oAssemblyName & "_" & "0" & "_" & geotextile)
        geotxtP13 = geotxtPoints.Add(foundatP2.Offset, foundatP2.Elevation, oAssemblyName & "_" & "0" & "_" & geotextile)
        geotxtP14 = geotxtPoints.Add(foundatP7.Offset, foundatP7.Elevation, oAssemblyName & "_" & "0" & "_" & geotextile & 2)
        geotxtP15 = geotxtPoints.Add(foundatP7.Offset, foundatP7.Elevation + geogridElev, oAssemblyName & "_" & "0" & "_" & geotextile)
        geotxtP16 = geotxtPoints.Add(geotxtP15.Offset + flip * geotextileOverlap, geotxtP15.Elevation, oAssemblyName & "_" & "0" & "_" & geotextile & 4)

        geotxtLink13 = geotxtLinks.Add(geotxtP11, geotxtP12, oAssemblyName & "_" & "0" & "_" & geotextile & "Down")
        geotxtLink14 = geotxtLinks.Add(geotxtP12, geotxtP13, oAssemblyName & "_" & "0" & "_" & geotextile & "Down")
        geotxtLink15 = geotxtLinks.Add(geotxtP13, geotxtP14, oAssemblyName & "_" & "0" & "_" & geotextile & "Down")
        geotxtLink16 = geotxtLinks.Add(geotxtP14, geotxtP15, oAssemblyName & "_" & "0" & "_" & geotextile & "Up")
        geotxtLink17 = geotxtLinks.Add(geotxtP15, geotxtP16, oAssemblyName & "_" & "0" & "_" & geotextile & "Up")
    End Sub
End Class
