Imports DBTransactionManager = Autodesk.AutoCAD.DatabaseServices.TransactionManager
Imports System.Math
Public Class SubbaseTW
    Inherits SATemplate

    ' *************************************************************************
    ' *************************************************************************
    ' *************************************************************************
    '          Name: 
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
    '                ТолщинаСлоя               double      no          0.3              width of geogrids
    '                ШиринаПризмы              double      no          3.0              step of geogrid layer
    '                ОтступОтОси               double      no          1.0              0
    '                Насыпь/Выемка             bool        no           3               0
    '                Кол-во слоев              long        no           2               0
    '                ШагФундаментов            double      no          0.5              step of geogrid layer
    '
    '
    'Output Parameters: Name               Type              Description
    '                ------------------------------------------------------------------
    '                None

    Private Const dFaceAngleDefault = 0.01
    Private Const SideDefault = Utilities.Right  '"right"
    Private Const dGridWidthDefault = 3.0
    Private Const WidthDefaultF = 0.8
    Private Const HeightDefaultF = 0.3
    Private Const dSubAsNameDefault = "Участок"
    Private Const dGeotextileOverlapDefault = 0.3
    Private Const dPrepHeight = 0.02
    Private Const dGeogridElevDefault = 0.15
    Private Const dFaceWidthDefault = 0.22
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

        Dim paramsBool As ParamBoolCollection
        paramsBool = corridorState.ParamsBool

        ' Add input parameters we used in this script
        paramsLong.Add(Utilities.Side, SideDefault)
        paramsDouble.Add("Длина георешеток", dGridWidthDefault)
        'paramsDouble.Add("Ширина Фундамента", WidthDefaultF)
        'paramsDouble.Add("Высота Фундамента", HeightDefaultF)
        paramsDouble.Add("Наклон лицевой грани", dFaceAngleDefault)
        paramsString.Add("Имя участка", dSubAsNameDefault)
        paramsDouble.Add("Перехлест геотекстиля", dGeotextileOverlapDefault)
        paramsDouble.Add("Шаг До Решетки", dGeogridElevDefault)
        'paramsDouble.Add("Толщина Облицовки", dFaceWidthDefault)
        paramsDouble.Add("Толщина Подготовки", dPrepHeight)
        'paramsDouble.Add("Толщина слоя", HeightDefault)
        'paramsDouble.Add("Шаг фундаментов", dFoundatStep)
        'paramsDouble.Add("Отступ от оси", dSlopeOffset)
        'paramsDouble.Add("Отступ ступени вдоль оси", dStepOffset)
        'paramsDouble.Add("Заложение откоса", dStepSlope)
    End Sub
    Protected Overrides Sub GetLogicalNamesImplement(corridorState As CorridorState)
        MyBase.GetLogicalNamesImplement(corridorState)

        'retrieve paramater buckets from the corridor state
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong
        'add logical names we used to script
        Dim ParamLong As ParamLong

        ParamLong = paramsLong.Add("Граница засыпки", ParamLogicalNameType.OffsetTarget)
        ParamLong.DisplayName = "Граница щебеночной подготовки"

    End Sub
    Protected Overrides Sub DrawImplement(ByVal corridorState As CorridorState)

        Dim tm As DBTransactionManager
        tm = Autodesk.AutoCAD.DatabaseServices.HostApplicationServices.WorkingDatabase.TransactionManager

        Dim oParamsOffsetTarget As ParamOffsetTargetCollection
        oParamsOffsetTarget = corridorState.ParamsOffsetTarget

        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble

        Dim paramsString As ParamStringCollection
        paramsString = corridorState.ParamsString
#Region "Присваиваем значения входных параметров переменным"
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
        Dim widthF As Double
        Try
            widthF = paramsDouble.Value("Ширина Фундамента")
        Catch
            widthF = WidthDefaultF
        End Try
        '-----------------------------------------
        Dim heightF As Double
        Try
            heightF = paramsDouble.Value("Высота Фундамента")
        Catch
            heightF = HeightDefaultF
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
        Dim oGridWidth As Double
        Try
            oGridWidth = paramsDouble.Value("Длина георешеток")
        Catch
            oGridWidth = dGridWidthDefault
        End Try
        '----------------------------------------
        Dim oFaceAngle As Double
        Try
            oFaceAngle = paramsDouble.Value("Наклон лицевой грани")
        Catch
            oFaceAngle = dFaceAngleDefault
        End Try
        '----------------------------------------
        Dim faceWidth As Double
        Try
            faceWidth = paramsDouble.Value("Толщина Облицовки")
        Catch
            faceWidth = dFaceWidthDefault
        End Try
        '----------------------------------------
        Dim oSubAsName As String
        Try
            oSubAsName = paramsString.Value("Имя участка")
        Catch
            oSubAsName = dSubAsNameDefault
        End Try
        '----------------------------------------
        Dim geotextileOverlap As Double
        Try
            geotextileOverlap = paramsDouble.Value("Перехлест Геотекстиля")
        Catch
            geotextileOverlap = dGeotextileOverlapDefault
        End Try
#End Region
        Dim concLeveling As String = "Цементная подготовка"
        Dim foundationConcrete As String = "Фундамент"
        Dim soil As String = "Дренирующий грунт"
        Dim geotextile As String = "geotextile"
        Dim gidro As String = "gidroizolUp"
        Dim gridName As String = "Триакс"
        Dim solidName As String = "Щебень основания"
        Dim pitName As String = "Котлован"
        Dim slopeName As String = "Откосы"

        Dim oOrigin As New PointInMem
        Dim oCurrentAlignmentId As ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, oCurrentAlignmentId, oOrigin)

        Dim dWallWidthOffset As Double = (oGridWidth + 1.0) * flip 'soil width
        Dim hasWallOffsetTarget As Boolean
        hasWallOffsetTarget = False

        If corridorState.Mode <> CorridorMode.Layout Then 'для сечений коридора
            Dim offsetTarget As WidthOffsetTarget
            Try
                offsetTarget = oParamsOffsetTarget.Value("Граница засыпки")
            Catch
                offsetTarget = Nothing
            End Try

            Dim xOffset As Double
            Dim yOffset As Double
            Dim soilOffset As Double

            If Not offsetTarget Is Nothing Then
                Try
                    Utilities.CalcAlignmentOffsetToThisAlignment(oCurrentAlignmentId, corridorState.CurrentStation, offsetTarget, soilOffset, xOffset, yOffset)
                    hasWallOffsetTarget = True
                    dWallWidthOffset = soilOffset - oOrigin.Offset
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "Граница засыпки", "RetainWallHorizontal")
                End Try
            End If

            Foundation(corridorState, flip, widthF, heightF, faceWidth, prepHeight, oSubAsName, foundationConcrete, concLeveling, gidro)
            SubBase(corridorState, flip, widthF, heightF, dWallWidthOffset, gridName, solidName, pitName, geotextile, geotextileOverlap, geogridElev, faceWidth, prepHeight, oFaceAngle, hasWallOffsetTarget, oSubAsName)
        Else 'LayoutMode
            Foundation(corridorState, flip, widthF, heightF, faceWidth, prepHeight, oSubAsName, foundationConcrete, concLeveling, gidro)
            SubBase(corridorState, flip, widthF, heightF, dWallWidthOffset, gridName, solidName, pitName, geotextile, geotextileOverlap, geogridElev, faceWidth, prepHeight, oFaceAngle, hasWallOffsetTarget, oSubAsName)
        End If

        Dim param As IParam
        param = paramsLong.Add(Utilities.Side, side)
        'param = paramsDouble.Add("Ширина Фундамента", widthF)
        'param = paramsDouble.Add("Высота Фундамента", heightF)
        param = paramsDouble.Add("Наклон лицевой грани", oFaceAngle)
        param = paramsString.Add("Имя участка", oSubAsName)
        param = paramsDouble.Add("Перехлест Геотекстиля", geotextileOverlap)
        param = paramsDouble.Add("Шаг До Решетки", geogridElev)
        'param = paramsDouble.Add("Толщина Облицовки", faceWidth)
        param = paramsDouble.Add("Толщина Подготовки", prepHeight)
        param = paramsDouble.Add("Длина георешеток", oGridWidth)
    End Sub

    'метод для создания фундаментного блока
    Public Sub Foundation(corridorState As CorridorState,
                                 flip As Double,
                                 fWidth As Double,
                                 fHeight As Double,
                                 blockWidth As Double,
                                 prepHeight As Double,
                                 oSubAsName As String,
                                 foundationConcrete As String,
                                 concLeveling As String,
                                 gidro As String)
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

        Dim foundShape As Autodesk.Civil.DatabaseServices.Shape
        Dim prepareShape As Autodesk.Civil.DatabaseServices.Shape
        '--------------------------------------------------------
        'создаем фундамент
        foundatP1 = foundPoints.Add(0, 0, "")
        foundatP2 = foundPoints.Add(foundatP1.Offset + fWidth / 2 * flip, foundatP1.Elevation - prepHeight, "")
        foundatP3 = foundPoints.Add(foundatP2.Offset, foundatP2.Elevation - fHeight, "")
        foundatP4 = foundPoints.Add(foundatP3.Offset - fWidth * flip, foundatP3.Elevation, "")
        foundatP5 = foundPoints.Add(foundatP4.Offset, foundatP4.Elevation + fHeight, "")
        foundatP6 = foundPoints.Add(foundatP1.Offset, foundatP1.Elevation - fHeight - prepHeight, "Ось фундамента")

        foundatLink1 = foundLinks.Add(foundatP2, foundatP3, "")
        foundatLink2 = foundLinks.Add(foundatP3, foundatP4, "")
        foundatLink3 = foundLinks.Add(foundatP4, foundatP5, "")
        foundatLink4 = foundLinks.Add(foundatP5, foundatP2, "")

        foundShape = Shapes.Add(foundLinks.ToArray, oSubAsName & "_" & foundationConcrete)

        Dim off = foundatP1.Offset
        Dim ele = foundatP1.Elevation
        'создаем подготовку под блоки
        foundatP7 = preparePoints.Add(foundatP1.Offset + blockWidth / 2 * flip, foundatP1.Elevation, "")
        foundatP8 = preparePoints.Add(foundatP1.Offset - blockWidth / 2 * flip, foundatP1.Elevation, "")

        helpPoint = foundPoints.Add(foundatP7.Offset, foundatP2.Elevation, "")

        foundatLink5 = prepareLinks.Add(foundatP2, foundatP7, "")
        foundatLink6 = prepareLinks.Add(foundatP7, foundatP8, "")
        foundatLink7 = prepareLinks.Add(foundatP8, foundatP5, "")
        foundatLink8 = prepareLinks.Add(foundatP5, foundatP2, "")

        prepareShape = Shapes.Add(foundatLink5, foundatLink6, foundatLink7, foundatLink8, oSubAsName & "_" & concLeveling)
        'создаем гидроизоляцию
        gidroP1 = gidroPoints.Add(foundatP4.Offset - 0.001, foundatP4.Elevation, oSubAsName & "_" & "0" & "_" & gidro & 1)
        gidroP2 = gidroPoints.Add(foundatP5.Offset, foundatP5.Elevation, "")
        gidroP3 = gidroPoints.Add(foundatP8.Offset, foundatP8.Elevation, "")
        gidroP4 = gidroPoints.Add(foundatP7.Offset, foundatP7.Elevation, "")
        gidroP5 = gidroPoints.Add(foundatP2.Offset, foundatP2.Elevation, "")
        gidroP6 = gidroPoints.Add(foundatP3.Offset + 0.001, foundatP3.Elevation, oSubAsName & "_" & "0" & "_" & gidro & 2)

        gidroL1 = gidroLinks.Add(gidroP1, gidroP2, oSubAsName & "_" & "0" & "_" & gidro)
        gidroL2 = gidroLinks.Add(gidroP2, gidroP3, oSubAsName & "_" & "0" & "_" & gidro)
        gidroL3 = gidroLinks.Add(gidroP3, gidroP4, oSubAsName & "_" & "0" & "_" & gidro)
        gidroL4 = gidroLinks.Add(gidroP4, gidroP5, oSubAsName & "_" & "0" & "_" & gidro)
        gidroL5 = gidroLinks.Add(gidroP5, gidroP6, oSubAsName & "_" & "0" & "_" & gidro)


    End Sub
    'метод для создания щебеночной подушки
    Public Sub SubBase(corridorState As CorridorState,
                       flip As Double,
                       fWidth As Double,
                       fHeight As Double,
                       tWidth As Double,
                       gridName As String,
                       solidName As String,
                       pitName As String,
                       geotextile As String,
                       geotextileOverlap As Double,
                       geogridElev As Double,
                       blockWidth As Double,
                       prepHeight As Double,
                       faceAngle As Double,
                       hasTargetOffset As Boolean,
                       oSubAsName As String)
        '---------------------------------------------------------
        'Create points
        '---------------------------------------------------------
        'объявляем коллекции точек, связей и форм
        Dim subPoints As PointCollection = corridorState.Points
        Dim subLinks As LinkCollection = corridorState.Links
        Dim Shapes As ShapeCollection
        Shapes = corridorState.Shapes
        '------------------------------------
        Dim gridPoints As PointCollection
        gridPoints = corridorState.Points
        Dim gridLinks As LinkCollection
        gridLinks = corridorState.Links
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
        Dim subP1 As Point
        Dim subP2 As Point
        Dim subP3 As Point
        Dim subP4 As Point
        Dim subP5 As Point
        Dim subP6 As Point
        Dim subP7 As Point
        Dim subP8 As Point
        Dim subP9 As Point
        Dim subP10 As Point
        Dim subP11 As Point
        Dim subP12 As Point

        Dim subL1 As Link
        Dim subL2 As Link
        Dim subL3 As Link
        Dim subL4 As Link
        Dim subL5 As Link
        Dim subL6 As Link
        Dim subL7 As Link
        Dim subL8 As Link
        Dim subL9 As Link
        Dim subL10 As Link
        Dim subL11 As Link
        Dim subL12 As Link

        Dim geotxtLink13 As Link
        Dim geotxtLink14 As Link
        Dim geotxtLink15 As Link
        Dim geotxtLink16 As Link


        Dim gravShape As Autodesk.Civil.DatabaseServices.Shape
        Dim faceSlope As Double = faceAngle * (Math.PI / 180)
        Dim fOffset As Double = geogridElev * Tan(faceSlope) * flip
        'создаем щебеночную подушку
        subP1 = subPoints.Add(fWidth / 2 * flip, -prepHeight, "")
        subP2 = subPoints.Add(subP1.Offset, subP1.Elevation - fHeight, "")
        subP3 = subPoints.Add(subP2.Offset - flip * fWidth, subP2.Elevation, "")
        subP4 = subPoints.Add(subP3.Offset, subP1.Elevation, "")
        subP5 = subPoints.Add(subP4.Offset - flip * 0.3, subP4.Elevation, "")
        subP6 = subPoints.Add(subP5.Offset - flip * 0.3, subP5.Elevation - 0.3, "")
        subP7 = subPoints.Add(subP6.Offset + flip * 0.15, subP6.Elevation - 0.3, "")
        subP8 = subPoints.Add(subP7.Offset + flip * 0.15, subP7.Elevation - 0.3, pitName)
        subP9 = subPoints.Add(subP8.Offset + flip * 1.5, subP8.Elevation, pitName)
        subP10 = subPoints.Add(subP9.Offset + flip * 0.3, subP9.Elevation + 0.3, "")
        subP11 = subPoints.Add(tWidth, subP10.Elevation, "")
        If hasTargetOffset Then
            subP12 = subPoints.Add(subP11.Offset, subP1.Elevation, "")
        Else
            subP12 = subPoints.Add(subP11.Offset + flip * 0.6, subP1.Elevation, "")
        End If
        Dim upperLinks As String = "crushedStone_Up"
        Dim lowerLinks As String = "crushedStone_Down"
        subL1 = subLinks.Add(subP1, subP2, upperLinks)
        subL2 = subLinks.Add(subP2, subP3, upperLinks)
        subL3 = subLinks.Add(subP3, subP4, upperLinks)
        subL4 = subLinks.Add(subP4, subP5, upperLinks)
        subL5 = subLinks.Add(subP5, subP6, upperLinks)
        subL6 = subLinks.Add(subP6, subP7, lowerLinks)
        subL7 = subLinks.Add(subP7, subP8, lowerLinks)
        subL8 = subLinks.Add(subP8, subP9, lowerLinks)
        subL9 = subLinks.Add(subP9, subP10, lowerLinks)
        subL10 = subLinks.Add(subP10, subP11, lowerLinks)
        subL11 = subLinks.Add(subP11, subP12, lowerLinks)
        subL12 = subLinks.Add(subP12, subP1, upperLinks)

        Dim links As Link() = {subL1, subL2, subL3, subL4, subL5, subL6, subL7, subL8, subL9, subL10, subL11, subL12}
        gravShape = Shapes.Add(links, oSubAsName & "_" & solidName)

        'создаем геотекстиль

        Dim geotxtP1 As Point = geotxtPoints.Add(subP12.Offset, subP12.Elevation, oSubAsName & "_" & "0" & "_" & geotextile & 1)
        Dim geotxtP2 As Point = geotxtPoints.Add(subP1.Offset, subP1.Elevation, oSubAsName & "_" & "0" & "_" & geotextile)
        Dim geotxtP3 As Point = geotxtPoints.Add(flip * blockWidth / 2, 0, oSubAsName & "_" & "0" & "_" & geotextile & 2)
        Dim geotxtP4 As Point = geotxtPoints.Add(geotxtP3.Offset + flip * faceSlope * geogridElev, geogridElev, oSubAsName & "_" & "0" & "_" & geotextile)
        Dim geotxtP5 As Point = geotxtPoints.Add(geotxtP4.Offset + flip * geotextileOverlap, geotxtP4.Elevation, oSubAsName & "_" & "0" & "_" & geotextile & 4)


        geotxtLink13 = geotxtLinks.Add(geotxtP1, geotxtP2, oSubAsName & "_" & "0" & "_" & geotextile & "Down")
        geotxtLink14 = geotxtLinks.Add(geotxtP2, geotxtP3, oSubAsName & "_" & "0" & "_" & geotextile & "Down")
        geotxtLink15 = geotxtLinks.Add(geotxtP3, geotxtP4, oSubAsName & "_" & "0" & "_" & geotextile & "Up")
        geotxtLink16 = geotxtLinks.Add(geotxtP4, geotxtP5, oSubAsName & "_" & "0" & "_" & geotextile & "Up")

        'создаем георешетки
        Dim lowGrid1 As Point = gridPoints.Add(subP8.Offset, subP8.Elevation, "lowerGrid1")
        Dim lowGrid2 As Point = gridPoints.Add(subP9.Offset, subP9.Elevation, "lowerGrid2")
        Dim lowGrid As Link = gridLinks.Add(lowGrid1, lowGrid2, "lowerGrid")

        Dim medGrid1 As Point = gridPoints.Add(subP7.Offset, subP7.Elevation, "medGrid1")
        Dim medGrid2 As Point = gridPoints.Add(subP11.Offset, subP11.Elevation, "medGrid2")
        Dim medGrid As Link = gridLinks.Add(medGrid1, medGrid2, "medGrid")

        Dim upGrid1 As Point = gridPoints.Add(subP2.Offset, subP2.Elevation, "upGrid1")
        Dim upGrid2 As Point = gridPoints.Add((subP12.Offset + subP11.Offset) / 2, subP2.Elevation, "upGrid2")
        If hasTargetOffset Then
            upGrid2 = gridPoints.Add(subP11.Offset, subP2.Elevation, "upGrid2")
        End If
        Dim upGrid As Link = gridLinks.Add(upGrid1, upGrid2, "upGrid")


    End Sub
End Class
